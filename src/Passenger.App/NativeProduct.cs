using A = Passenger.App.AppActions;
using W = Passenger.App.ToolWindowIds;

namespace Passenger.App;

internal static class NativeProduct
{
    internal static bool ManagedPayloadAvailable =>
#if PITSTOP_NATIVE_DISTRIBUTION
        false;
#else
        true;
#endif

    internal static bool ManagedToolWindow(string id) => id is W.Debug or W.Tests or W.NuGet or W.Profiler or W.LiveTree or W.Game or W.Aspire or W.Migrations or W.Endpoints or W.StepLibrary;

    internal static bool Offers(IdeAction action) => action.Group is not ("Debug" or "NuGet" or "Profiler" or "Unit Tests" or "Godot" or "Aspire" or "Migrations")
        && action.Id is not (A.Debug or A.DebugAnotherCopy or A.DebugInTerminal or A.DebugHere or A.Profile or A.ProfileInTerminal or A.ProfileHere
            or A.RunCurrentScene or A.ShowDebug or A.ShowProfiler or A.ShowTests or A.ShowNuGet or A.ShowAspire or A.ShowMigrations
            or A.ShowLiveTree or A.ShowGame or A.ShowEndpoints or A.ShowStepLibrary or A.ViewBreakpoints or A.AttachToProcess
            or A.SaveAsLaunchProfile or A.Publish or A.PublishAgain or A.ToggleSolutionAnalysis or A.XamlPreview or A.BuildForPreview
            or A.TargetFramework or A.NewType or A.NewItem or A.AddExistingProject or A.AddReference or A.AddAspireOrchestration
            or A.ManageNuGet or A.ManageNuGetForSolution or A.EditProjectFile or A.UnloadProject or A.ReloadProject or A.IncludeInProject or A.ExcludeFromProject);
}
