using LessonsPlayer.Core.Settings;

namespace LessonsPlayer.Core.Abstractions;

/// <summary>Persistência das preferências do usuário.</summary>
public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}
