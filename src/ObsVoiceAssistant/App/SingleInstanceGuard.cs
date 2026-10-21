namespace ObsVoiceAssistant.Application;

/// <summary>
/// Не даёт запустить второй экземпляр приложения. Два экземпляра создали бы
/// два глобальных обработчика Caps Lock и два потока непрерывного прослушивания,
/// поэтому одна и та же команда выполнялась бы дважды.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>Имя именованного объекта; действует в пределах текущего сеанса Windows.</summary>
    public const string DefaultName = @"Local\ObsVoiceAssistant.SingleInstance";

    private Mutex? mutex;

    public SingleInstanceGuard(string name = DefaultName)
    {
        // initiallyOwned: false — достаточно самого факта существования объекта.
        // Владение мьютексом привязано к потоку, а guard живёт дольше одного вызова,
        // поэтому захват не нужен: Dispose просто закрывает handle.
        mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        IsPrimary = createdNew;
        if (!createdNew)
        {
            mutex.Dispose();
            mutex = null;
        }
    }

    /// <summary>Приложение запущено впервые и может продолжать работу.</summary>
    public bool IsPrimary { get; }

    public void Dispose()
    {
        mutex?.Dispose();
        mutex = null;
    }
}
