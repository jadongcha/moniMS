namespace MoniMS.Core.Presets;

public interface IPresetStore
{
    string RootDirectory { get; }

    IReadOnlyList<Preset> GetAll();

    Preset? Get(string id);

    void Save(Preset preset);

    void Delete(string id);

    /// <summary>프리셋별 폴더 (배경화면 복사본 저장 위치). 없으면 생성.</summary>
    string GetPresetDirectory(string id);
}
