using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.UnitTests;

public sealed class CommandCorrectionTests
{
    private readonly CommandParser parser = new();

    [Theory]
    [InlineData("начни запсь", CommandIntent.StartRecording)]
    [InlineData("остонови запись", CommandIntent.StopRecording)]
    [InlineData("запусти запись", CommandIntent.StartRecording)]
    public void Parse_OneLetterError_IsCorrected(string text, CommandIntent expected)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
    }

    [Fact]
    public void Parse_TwoLetterErrorInLongWord_IsCorrected()
    {
        // «прикроти» отличается от «прекрати» на две буквы.
        var result = parser.Parse("прикроти запись");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.StopRecording, result.Intent);
    }

    [Fact]
    public void Parse_CorrectionIsReportedForDiagnostics()
    {
        var result = parser.ParseWithDetails("остонови запись");

        Assert.True(result.Recognized);
        Assert.Contains(result.Corrections, correction => correction.From == "остонови");
        Assert.Contains(result.Corrections, correction => correction.To == "останови");
    }

    [Fact]
    public void Parse_WordAmbiguousButCommandSame_IsExecuted()
    {
        // «запису» одинаково близко к «записи» и «запись», но обе трактовки
        // дают одну и ту же команду, поэтому выполнять её можно.
        var result = parser.Parse("останови запису");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.StopRecording, result.Intent);
    }

    [Fact]
    public void Parse_AmbiguousBetweenOppositeCommands_IsRejected()
    {
        // «ыключи» на равном расстоянии от «включи» и «выключи»:
        // для браузера это противоположные команды, поэтому угадывать нельзя.
        Assert.Null(parser.Parse("ыключи браузер"));
    }

    [Fact]
    public void Parse_AmbiguousWordingOfToggle_IsSafe()
    {
        // Для микрофона «включи» и «выключи» — одно и то же действие,
        // поэтому та же двусмысленность здесь безопасна.
        Assert.Equal(CommandIntent.ToggleInput, parser.Parse("ыключи микрофон")?.Intent);
    }

    [Theory]
    [InlineData("нашли запись", CommandIntent.StartRecording, null)]
    [InlineData("основе запись", CommandIntent.StopRecording, null)]
    [InlineData("основании запись", CommandIntent.StopRecording, null)]
    [InlineData("охране повтор", CommandIntent.SaveReplay, null)]
    [InlineData("замок микрофон", CommandIntent.ToggleInput, "microphone")]
    [InlineData("начни с три", CommandIntent.StartStream, null)]
    [InlineData("начни стареем", CommandIntent.StartStream, null)]
    [InlineData("начать рим", CommandIntent.StartStream, null)]
    [InlineData("начни с прим", CommandIntent.StartStream, null)]
    [InlineData("начнем трансляцию", CommandIntent.StartStream, null)]
    [InlineData("ночные стрим", CommandIntent.StartStream, null)]
    [InlineData("установи записи", CommandIntent.StopRecording, null)]
    [InlineData("включи музыку навстречу", CommandIntent.UnmuteInput, "stream_music")]
    [InlineData("стп стрим", CommandIntent.StopStream, null)]
    public void Parse_PhraseLevelErrors_AreRecognized(string text, CommandIntent expected, string? target)
    {
        // Vosk иногда переставляет, сливает или сильно искажает слова. Если
        // остальная часть фразы однозначна, пользователю не нужно повторять.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal(target, result.Target);
    }

    [Theory]
    [InlineData("включи музыку потише")]
    [InlineData("включи музыку громче")]
    [InlineData("выключи музыку совсем")]
    [InlineData("начни не трансляцию")]
    [InlineData("это запись")]
    [InlineData("вот стрим")]
    [InlineData("выключи окно")]
    [InlineData("включи видео")]
    [InlineData("запись это")]
    [InlineData("стрим это")]
    [InlineData("что ли")]
    [InlineData("на окко")]
    [InlineData("включи все")]
    [InlineData("включи весь звук")]
    [InlineData("включи все звуки")]
    [InlineData("верни все")]
    [InlineData("размут все")]
    [InlineData("размуть все")]
    public void Parse_PhraseLevelGuess_DoesNotInventExtraMeaning(string text)
    {
        // Лишнее слово или отрицание меняет смысл. Такие фразы лучше отклонить,
        // чем выполнить не то действие.
        Assert.Null(parser.Parse(text));
    }

    [Fact]
    public void Parse_PhraseLevelCorrection_IsReportedForDiagnostics()
    {
        var result = parser.ParseWithDetails("нашли запись");

        Assert.True(result.Recognized);
        Assert.Contains(
            result.Corrections,
            correction => correction.From == "нашли запись" && correction.To == "начни запись");
    }

    [Theory]
    [InlineData("стоп записи", CommandIntent.StopRecording)]
    [InlineData("стоп стрима", CommandIntent.StopStream)]
    [InlineData("запись остановить", CommandIntent.StopRecording)]
    [InlineData("микрофон выключить", CommandIntent.ToggleInput)]
    [InlineData("микрофон включить", CommandIntent.ToggleInput)]
    [InlineData("повтор сохранить", CommandIntent.SaveReplay)]
    public void Parse_NaturalInfinitiveAndGenitiveForms_AreSupported(string text, CommandIntent expected)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
    }

    [Fact]
    public void SuggestClosest_ForUnknownPhrase_ReturnsNearbyCommand()
    {
        // «же» — не заполнитель и не команда: фраза не разберётся,
        // но подсказка должна показать, что имелось в виду.
        Assert.Null(parser.Parse("включи браузер же"));

        var suggestion = parser.SuggestClosest("включи браузер же");

        Assert.NotNull(suggestion);
        Assert.Equal(CommandIntent.UnmuteInput, suggestion.Phrase.Intent);
        Assert.Equal("browser", suggestion.Phrase.Target);
    }

    [Theory]
    [InlineData("не")]
    [InlineData("ни")]
    [InlineData("мок")]
    public void SuggestClosest_ForShortOrNegatedPhrase_ReturnsNull(string text)
    {
        // «Не» не должно подсказывать «мут», а короткое слово — приглашать
        // добавить себя в словарь: такое решение принимает человек.
        Assert.Null(parser.SuggestClosest(text));
    }

    [Theory]
    [InlineData("заглуши браузер", CommandIntent.MuteInput)]
    [InlineData("мут браузер", CommandIntent.MuteInput)]
    [InlineData("выключи звук браузера", CommandIntent.MuteInput)]
    [InlineData("браузер отключи", CommandIntent.MuteInput)]
    [InlineData("верни браузер", CommandIntent.UnmuteInput)]
    [InlineData("размуть браузер", CommandIntent.UnmuteInput)]
    [InlineData("включи звук браузера", CommandIntent.UnmuteInput)]
    [InlineData("сними мут с браузера", CommandIntent.UnmuteInput)]
    [InlineData("замуть браузер", CommandIntent.MuteInput)]
    [InlineData("замьютить браузер", CommandIntent.MuteInput)]
    [InlineData("убери браузер", CommandIntent.MuteInput)]
    [InlineData("подключить браузер", CommandIntent.UnmuteInput)]
    [InlineData("врубай браузер", CommandIntent.UnmuteInput)]
    public void Parse_BrowserPhrases_KeepExplicitMeaning(string text, CommandIntent expected)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal("browser", result.Target);
    }

    [Fact]
    public void Parse_MicrophoneToggles_BrowserKeepsExplicitMeaning()
    {
        // Микрофон переключается любой из формулировок: пользователю не нужно
        // помнить, включён он сейчас или нет.
        Assert.Equal(CommandIntent.ToggleInput, parser.Parse("включи микрофон")?.Intent);
        Assert.Equal(CommandIntent.ToggleInput, parser.Parse("выключи микрофон")?.Intent);
        Assert.Equal(CommandIntent.ToggleInput, parser.Parse("включи микрофн")?.Intent);
        Assert.Equal(CommandIntent.ToggleInput, parser.Parse("выключи микрофн")?.Intent);

        // Браузер сохраняет явный смысл: ошибка в одну букву не должна его переворачивать.
        Assert.Equal(CommandIntent.UnmuteInput, parser.Parse("включи браузер")?.Intent);
        Assert.Equal(CommandIntent.MuteInput, parser.Parse("выключи браузер")?.Intent);
    }

    [Theory]
    [InlineData("начни запись пожалуйста", CommandIntent.StartRecording)]
    [InlineData("пожалуйста останови запись", CommandIntent.StopRecording)]
    [InlineData("ну давай запусти стрим", CommandIntent.StartStream)]
    [InlineData("сохрани повтор пожалуйста", CommandIntent.SaveReplay)]
    public void Parse_FillerWords_AreIgnored(string text, CommandIntent expected)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
    }

    [Theory]
    [InlineData("блин включи микрофон", CommandIntent.ToggleInput, "microphone")]
    [InlineData("вчера останови запись", CommandIntent.StopRecording, null)]
    [InlineData("выключи бразуер", CommandIntent.MuteInput, "browser")]
    [InlineData("сохрани запись", CommandIntent.StopRecording, null)]
    [InlineData("начни запись видео", CommandIntent.StartRecording, null)]
    [InlineData("начни запись экрана", CommandIntent.StartRecording, null)]
    [InlineData("как-то начать трансляцию", CommandIntent.StartStream, null)]
    public void Parse_BroaderRecognition_IsStillUnambiguous(
        string text,
        CommandIntent expected,
        string? target)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal(target, result.Target);
    }

    [Theory]
    [InlineData("не начни запись")]
    [InlineData("не выключай микрофон")]
    [InlineData("не надо останавливать запись")]
    public void Parse_NegatedPhrase_IsRejected(string text)
    {
        // Отрицание меняет смысл, поэтому такая фраза не выполняется вообще.
        Assert.Null(parser.Parse(text));
    }

    [Theory]
    [InlineData("старт записи", CommandIntent.StartRecording)]
    [InlineData("запись стоп", CommandIntent.StopRecording)]
    [InlineData("стрим старт", CommandIntent.StartStream)]
    [InlineData("микрофон заглуши", CommandIntent.ToggleInput)]
    [InlineData("повтор сохрани", CommandIntent.SaveReplay)]
    [InlineData("буфер повторов включи", CommandIntent.StartReplayBuffer)]
    public void Parse_ReverseWordOrder_IsSupported(string text, CommandIntent expected)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
    }

    [Fact]
    public void Parse_ArgumentCommandWithError_IsCorrected()
    {
        var result = parser.Parse("измини категорию стрима на доту два");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.SetStreamCategory, result.Intent);
        Assert.Equal("доту два", result.Argument);
    }

    [Fact]
    public void Parse_CorrectedPrefix_KeepsArgumentExactly()
    {
        // Ошибка в префиксе исправляется, а название категории остаётся ровно таким,
        // как его сказали: «муть» не должно превратиться в «мут».
        var result = parser.Parse("измини категорию стрима на муть");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.SetStreamCategory, result.Intent);
        Assert.Equal("муть", result.Argument);
    }

    [Theory]
    [InlineData("замуть микро", CommandIntent.ToggleInput)]
    [InlineData("вернуть микро", CommandIntent.ToggleInput)]
    [InlineData("микрофон размуть", CommandIntent.ToggleInput)]
    public void Parse_GeneratedMicrophoneForms_AreSupported(string text, CommandIntent expected)
    {
        // Эти формы не перечислены руками: они собраны из глагола и источника.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal("microphone", result.Target);
    }

    [Theory]
    [InlineData("мут")]
    [InlineData("муть")]
    [InlineData("мьют")]
    [InlineData("муд")]
    [InlineData("мод")]
    [InlineData("мог")]
    [InlineData("мут микрофон")]
    [InlineData("мут микрофона")]
    [InlineData("мог микрофона")]
    public void Parse_ShortMicrophoneMuteForms_ToggleMicrophone(string text)
    {
        // «Мут» — короткая форма, которую Vosk иногда слышит как «муть» или
        // «мьют». Все варианты означают одно действие: переключить микрофон.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.ToggleInput, result.Intent);
        Assert.Equal("microphone", result.Target);
    }

    [Theory]
    [InlineData("ол мут")]
    [InlineData("олл мут")]
    [InlineData("ол муть")]
    [InlineData("олл муть")]
    [InlineData("олмут")]
    [InlineData("оллмут")]
    [InlineData("олмуть")]
    [InlineData("оллмуть")]
    [InlineData("ол мьют")]
    [InlineData("олл мьют")]
    [InlineData("олмьют")]
    [InlineData("оллмьют")]
    [InlineData("all mute")]
    [InlineData("allmute")]
    [InlineData("выключи весь звук")]
    [InlineData("выключить весь звук")]
    [InlineData("выключи все звуки")]
    [InlineData("заглуши все")]
    [InlineData("заглушить все")]
    [InlineData("мут все")]
    [InlineData("все мут")]
    public void Parse_MuteAllAudioForms_AreRecognized(string text)
    {
        // «Ол мут» и явные русские формулировки выключают весь звук OBS.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.MuteAllAudio, result.Intent);
        Assert.Null(result.Target);
    }

    [Theory]
    [InlineData("выключи музыку")]
    [InlineData("включи музыку")]
    [InlineData("выключи мою музыку")]
    [InlineData("включи мою музыку")]
    [InlineData("выключи личную музыку")]
    [InlineData("музыку выключи")]
    public void Parse_PersonalMusic_TogglesLikeMicrophone(string text)
    {
        // Слово «включи»/«выключи» не выбирает результат: личная музыка
        // переключается в противоположное состояние, как микрофон.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.ToggleInput, result.Intent);
        Assert.Equal("personal_music", result.Target);
    }

    [Theory]
    [InlineData("выключи музыку стрима", CommandIntent.MuteInput)]
    [InlineData("включи музыку стрима", CommandIntent.UnmuteInput)]
    [InlineData("заглуши музыку стрима", CommandIntent.MuteInput)]
    [InlineData("верни музыку стрима", CommandIntent.UnmuteInput)]
    [InlineData("выключи звук стрима", CommandIntent.MuteInput)]
    [InlineData("включи звук стрима", CommandIntent.UnmuteInput)]
    [InlineData("музыку стрима выключи", CommandIntent.MuteInput)]
    public void Parse_StreamMusic_KeepsExplicitMeaning(string text, CommandIntent expected)
    {
        // Музыка стрима, как браузер, понимает mute/unmute буквально.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal("stream_music", result.Target);
    }

    [Fact]
    public void Parse_MusicWordWithWrongCase_IsCorrected()
    {
        // Vosk часто отдаёт именительный падеж: «выключи музыка».
        var result = parser.Parse("выключи музыка");

        Assert.NotNull(result);
        Assert.Equal(CommandIntent.ToggleInput, result.Intent);
        Assert.Equal("personal_music", result.Target);
    }

    [Theory]
    [InlineData("выключи музыку стрим", CommandIntent.MuteInput)]
    [InlineData("включи звук стрим", CommandIntent.UnmuteInput)]
    [InlineData("выключи музыка стрим", CommandIntent.MuteInput)]
    public void Parse_StreamMusicWithShortStreamWord_IsCorrected(string text, CommandIntent expected)
    {
        // «стрим» вместо «стрима» — частая ошибка Vosk. Команда должна остаться
        // музыкой стрима, а не остановкой трансляции.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal("stream_music", result.Target);
    }

    [Theory]
    [InlineData("выключи музыку на стриме", CommandIntent.MuteInput)]
    [InlineData("включи музыку на стриме", CommandIntent.UnmuteInput)]
    [InlineData("выключи музыку на стрима", CommandIntent.MuteInput)]
    [InlineData("включи звук на стрим", CommandIntent.UnmuteInput)]
    [InlineData("выключи музыку на стремя", CommandIntent.MuteInput)]
    [InlineData("включи музыку на стриму", CommandIntent.UnmuteInput)]
    [InlineData("выключи музыку на стримов", CommandIntent.MuteInput)]
    [InlineData("выключи музыку стримов", CommandIntent.MuteInput)]
    [InlineData("музыку на стриме выключи", CommandIntent.MuteInput)]
    public void Parse_StreamMusicWithPreposition_KeepsExplicitMeaning(string text, CommandIntent expected)
    {
        // «на стриме» — обычная формулировка пользователя. Vosk слышит её как
        // «на стремя», «на стриму» и «на стримов», поэтому формы с предлогом
        // и без него добавлены в источник явно.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal("stream_music", result.Target);
    }

    [Theory]
    [InlineData("включи стрим", CommandIntent.StartStream)]
    [InlineData("выключи стрим", CommandIntent.StopStream)]
    [InlineData("останови стрим", CommandIntent.StopStream)]
    public void Parse_StreamCommands_StillMeanBroadcastNotMusic(string text, CommandIntent expected)
    {
        // Слово «стрим» без «музыки» остаётся управлением трансляцией.
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Null(result.Target);
    }

    [Theory]
    [InlineData("замуть музыку", CommandIntent.ToggleInput, "personal_music")]
    [InlineData("выруби мою музыку", CommandIntent.ToggleInput, "personal_music")]
    [InlineData("выруби звук стрима", CommandIntent.MuteInput, "stream_music")]
    [InlineData("верни звук стрим", CommandIntent.UnmuteInput, "stream_music")]
    public void Parse_GeneratedMusicForms_AreSupported(string text, CommandIntent expected, string target)
    {
        var result = parser.Parse(text);

        Assert.NotNull(result);
        Assert.Equal(expected, result.Intent);
        Assert.Equal(target, result.Target);
    }

    [Fact]
    public void Vocabulary_ContainsNoDuplicatePhrases()
    {
        // Явный список и сгенерированные формы не должны пересекаться.
        var duplicates = CommandVocabulary.Phrases
            .GroupBy(phrase => CommandVocabulary.Normalize(phrase.Phrase), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Vocabulary_DoesNotContainConflictingPhrases()
    {
        // Одна и та же фраза не должна означать разные команды или разные источники.
        var conflicts = CommandVocabulary.Phrases
            .GroupBy(phrase => CommandVocabulary.Normalize(phrase.Phrase), StringComparer.Ordinal)
            .Where(group => group.Select(phrase => (phrase.Intent, phrase.Target)).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Vocabulary_ShortWordsAreNeverCorrected()
    {
        // Слова из трёх букв не исправляются: «не» и «на» отличаются одной буквой.
        Assert.Null(parser.Parse("не запусти стрим"));
        Assert.Null(parser.Parse("на запусти стрим"));
    }
}
