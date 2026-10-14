namespace SaveEditor.Core;

/// <summary>Holds every registered game module; new games are added with <see cref="Register"/>.</summary>
public sealed class GameRegistry
{
    private readonly List<IGameModule> _modules = [];

    public IReadOnlyList<IGameModule> Modules => _modules;

    public GameRegistry Register(IGameModule module)
    {
        if (_modules.Any(m => m.Id == module.Id))
            throw new InvalidOperationException($"Module '{module.Id}' is already registered.");
        _modules.Add(module);
        return this;
    }

    /// <summary>Finds the first module that recognises the file, or null.</summary>
    public IGameModule? Detect(string path) => _modules.FirstOrDefault(m => m.CanOpen(path));
}
