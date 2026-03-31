using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._DragonStation.FighterProgression;
using Robust.Shared.Console;

namespace Content.Server._DragonStation.FighterProgression;

[AdminCommand(AdminFlags.Debug)]
public sealed class ResetFighterProgressCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "resetfighterprogress";
    public string Description => "Resets the attached character's fighter progression and hidden transformation progress.";
    public string Help => "resetfighterprogress";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteLine("This command does not take arguments.");
            return;
        }

        var player = shell.Player;
        if (player?.AttachedEntity is not { Valid: true } uid)
        {
            shell.WriteLine("You must be attached to a character to use this command.");
            return;
        }

        if (!_entities.TryGetComponent<FighterProgressionComponent>(uid, out var component))
        {
            shell.WriteLine("Your current character does not have fighter progression.");
            return;
        }

        if (!_entities.System<FighterProgressionSystem>().TryDebugResetProgression(uid, component))
        {
            shell.WriteLine("Failed to reset fighter progression.");
            return;
        }

        shell.WriteLine("Fighter progression reset.");
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed class AddFighterXpCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;

    public string Command => "addfighterxp";
    public string Description => "Adds an exact amount of fighter XP to the attached character.";
    public string Help => "addfighterxp <amount>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine("Usage: addfighterxp <amount>");
            return;
        }

        if (!int.TryParse(args[0], out var amount))
        {
            shell.WriteLine("Amount must be a number.");
            return;
        }

        if (amount <= 0)
        {
            shell.WriteLine("Amount must be greater than zero.");
            return;
        }

        var player = shell.Player;
        if (player?.AttachedEntity is not { Valid: true } uid)
        {
            shell.WriteLine("You must be attached to a character to use this command.");
            return;
        }

        var component = _entities.EnsureComponent<FighterProgressionComponent>(uid);

        if (!_entities.System<FighterProgressionSystem>().TryDebugAddXp(uid, amount, component))
        {
            shell.WriteLine("Failed to add fighter XP.");
            return;
        }

        shell.WriteLine($"Added {amount} fighter XP.");
    }
}
