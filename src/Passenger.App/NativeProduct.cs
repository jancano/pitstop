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

    internal static bool ManagedToolWindow(string id) => id is W.Debug or W.Tests or W.NuGet or W.Profiler or W.LiveTree or W.Game or W.Aspire or W.StepLibrary;

    internal static bool Offers(IdeAction action) => action.Group is not ("Debug" or "NuGet" or "Profiler" or "Unit Tests" or "Godot" or "Aspire"
        or "Services" or "Step Library")
        && action.Id is not (A.Debug or A.DebugAnotherCopy or A.DebugInTerminal or A.DebugHere or A.Profile or A.ProfileInTerminal or A.ProfileHere
            or A.RunCurrentScene or A.ShowDebug or A.ShowProfiler or A.ShowTests or A.ShowNuGet or A.ShowAspire
            or A.ShowLiveTree or A.ShowGame or A.ShowStepLibrary or A.ViewBreakpoints or A.AttachToProcess
            or A.NameOpenApiDocument
            or A.SaveAsLaunchProfile or A.ApplyHotReloadChanges or A.Publish or A.PublishAgain or A.ToggleSolutionAnalysis or A.RestartCSharpServer
            or A.ManageUserSecrets or A.InstallWorkloads or A.InstallAndroidDependencies or A.MakeMobileEdits or A.KeepAssembliesWhole
            or A.XamlPreview or A.BuildForPreview or A.OverlayFps or A.OverlayDirtyRects or A.OverlayLayoutTimeGraph or A.OverlayRenderTimeGraph
            or A.TargetFramework or A.NewType or A.NewItem or A.NewSolutionFolder or A.AddExistingProject or A.AddExistingItem or A.AddReference
            or A.AddAspireOrchestration or A.AddDockerSupport or A.AddDockerCompose
            or A.ManageNuGet or A.ManageNuGetForSolution or A.EditProjectFile or A.UnloadProject or A.ReloadProject or A.IncludeInProject or A.ExcludeFromProject);
}
