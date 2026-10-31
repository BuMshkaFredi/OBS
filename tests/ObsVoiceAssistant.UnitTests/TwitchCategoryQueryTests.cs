using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Integrations.Twitch;

namespace ObsVoiceAssistant.UnitTests;

public sealed class TwitchCategoryQueryTests
{
    [Theory]
    [InlineData("пас оф экзайл", "Path of Exile")]
    [InlineData("пас оф экзаил", "Path of Exile")]
    [InlineData("пое", "Path of Exile")]
    [InlineData("поэ", "Path of Exile")]
    [InlineData("пасов экзамен", "Path of Exile")]
    [InlineData("пассов экзамен", "Path of Exile")]
    [InlineData("пасов экзамена", "Path of Exile")]
    [InlineData("пас о экзамен", "Path of Exile")]
    [InlineData("путь изгнанника", "Path of Exile")]
    [InlineData("напои", "Path of Exile")]
    [InlineData("пое 2", "Path of Exile 2")]
    [InlineData("пое два", "Path of Exile 2")]
    [InlineData("пас оф экзайл два", "Path of Exile 2")]
    [InlineData("доту", "Dota 2")]
    [InlineData("доте", "Dota 2")]
    [InlineData("дота дс", "Dota 2")]
    [InlineData("дота дпс", "Dota 2")]
    [InlineData("майнкрафт", "Minecraft")]
    [InlineData("майн крафт", "Minecraft")]
    [InlineData("майн", "Minecraft")]
    [InlineData("айзек", "The Binding of Isaac")]
    [InlineData("айзэк", "The Binding of Isaac")]
    [InlineData("раст", "Rust")]
    [InlineData("руст", "Rust")]
    [InlineData("торчлайт инфинити", "Torchlight: Infinite")]
    [InlineData("джаст чатинг", "Just Chatting")]
    [InlineData("джаст чаттинг", "Just Chatting")]
    public void BuildVariants_KnownRussianName_UsesAlias(string spoken, string expected)
    {
        var variants = TwitchCategoryQuery.BuildVariants(spoken);

        Assert.Equal(expected, variants[0]);
    }

    [Fact]
    public void BuildVariants_Alias_DoesNotSendRussianQueryToTwitch()
    {
        // «пас оф экзайл» и его транслитерация всё равно ничего не найдут,
        // а случайный первый результат мог бы поставить не ту категорию.
        var variants = TwitchCategoryQuery.BuildVariants("пас оф экзайл");

        Assert.DoesNotContain("пас оф экзайл", variants);
        Assert.DoesNotContain("pas of ekzayl", variants);
    }

    [Theory]
    [InlineData("пас оф экзаль", "Path of Exile")]
    [InlineData("майкрафт", "Minecraft")]
    [InlineData("джаст чатин", "Just Chatting")]
    public void BuildVariants_RecognitionTypo_IsCorrectedByAlias(string spoken, string expected)
    {
        var variants = TwitchCategoryQuery.BuildVariants(spoken);

        Assert.Equal(expected, variants[0]);
    }

    [Fact]
    public void BuildVariants_NumberOutsideAlias_IsNotGuessed()
    {
        // «пое три» не должно приблизительно стать «Path of Exile 2».
        var variants = TwitchCategoryQuery.BuildVariants("пое три");

        Assert.DoesNotContain("Path of Exile 2", variants);
        Assert.DoesNotContain("Path of Exile", variants);
    }

    [Fact]
    public void BuildVariants_ShortUnknownWord_IsNotCorrected()
    {
        // «рост» — расстояние 1 от «раст», но слова из четырёх букв не исправляются:
        // иначе обычное слово станет названием игры.
        var variants = TwitchCategoryQuery.BuildVariants("рост");

        Assert.DoesNotContain("Rust", variants);
        Assert.Equal("рост", variants[0]);
    }

    [Fact]
    public void BuildVariants_LatinName_IsSearchedAsIs()
    {
        var variants = TwitchCategoryQuery.BuildVariants("Path of Exile");

        Assert.Equal(new[] { "Path of Exile" }, variants);
    }

    [Fact]
    public void BuildVariants_UnknownRussianName_KeepsTransliteration()
    {
        // Игры вне таблицы работают как раньше: транслитерация с цифрами.
        var variants = TwitchCategoryQuery.BuildVariants("дота два");

        Assert.Contains("dota 2", variants, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("ведьмак", "vedmak")]
    [InlineData("съезд", "sezd")]
    public void BuildVariants_NameWithSoftOrHardSign_IsTransliterated(string spoken, string expected)
    {
        // «ь» и «ъ» отображаются в пустую строку. Раньше «путь изгнанника»
        // ронял поиск категории с IndexOutOfRangeException (лог клиента 31.10.2026).
        var variants = TwitchCategoryQuery.BuildVariants(spoken);

        Assert.Contains(expected, variants, StringComparer.Ordinal);
    }

    [Fact]
    public void Aliases_SpokenNamesAreUnique()
    {
        var conflicts = TwitchCategoryAliases.All
            .SelectMany(alias => alias.SpokenNames.Select(
                spoken => (Spoken: spoken, Search: alias.SearchNames[0])))
            .GroupBy(entry => entry.Spoken, StringComparer.Ordinal)
            .Where(group => group.Select(entry => entry.Search)
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Aliases_SpokenNamesAreNormalizedAndHaveSearchNames()
    {
        // Ключ, записанный не в нормализованном виде (например, с «ё»),
        // никогда не совпадёт: поиск идёт по нормализованной фразе.
        var notNormalized = TwitchCategoryAliases.All
            .SelectMany(alias => alias.SpokenNames)
            .Where(spoken => !string.Equals(
                spoken,
                CommandVocabulary.Normalize(spoken),
                StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(notNormalized);
        Assert.All(TwitchCategoryAliases.All, alias => Assert.NotEmpty(alias.SearchNames));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildVariants_EmptyQuery_ReturnsNothing(string? query)
    {
        Assert.Empty(TwitchCategoryQuery.BuildVariants(query));
    }
}
