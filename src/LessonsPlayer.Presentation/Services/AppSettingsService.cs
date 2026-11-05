using LessonsPlayer.Core.Abstractions;
using LessonsPlayer.Core.Settings;

namespace LessonsPlayer.Presentation.Services;

/// <summary>
/// Mantém as preferências em memória, compartilhadas entre os ViewModels.
/// Alterações frequentes (ex.: volume) só vão para o disco no <see cref="Flush"/>.
/// </summary>
public sealed class AppSettingsService(ISettingsStore store)
{
    private bool _hasPendingChanges;

    public AppSettings Current { get; private set; } = store.Load();

    public void Update(Func<AppSettings, AppSettings> change, bool saveNow = false)
    {
        ArgumentNullException.ThrowIfNull(change);

        var updated = change(Current);
        if (updated == Current)
        {
            return;
        }

        Current = updated;
        _hasPendingChanges = true;
        if (saveNow)
        {
            Flush();
        }
    }

    public void Flush()
    {
        if (!_hasPendingChanges)
        {
            return;
        }

        store.Save(Current);
        _hasPendingChanges = false;
    }
}
