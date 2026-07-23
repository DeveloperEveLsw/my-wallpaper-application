namespace Wallpaper.Hosts;

internal sealed class WallpaperEngineInputRouterState
{
    public nint WorkerWindowHandle { get; set; }

    public nint DesktopViewWindowHandle { get; set; }

    public bool InteractiveWorkerRaised;

    public bool HasAppliedState { get; private set; }

    public bool InputEnabled { get; private set; }

    public bool HasTargetRectangle { get; private set; }

    public int TargetLeft { get; private set; }

    public int TargetTop { get; private set; }

    public int TargetRight { get; private set; }

    public int TargetBottom { get; private set; }

    public bool ShouldApply(bool enableInput, bool forceReconcile) =>
        forceReconcile ||
        !HasAppliedState ||
        InputEnabled != enableInput;

    public void SetTargetRectangle(int left, int top, int right, int bottom)
    {
        HasTargetRectangle = true;
        TargetLeft = left;
        TargetTop = top;
        TargetRight = right;
        TargetBottom = bottom;
    }

    public void ClearTargetRectangle()
    {
        HasTargetRectangle = false;
        TargetLeft = 0;
        TargetTop = 0;
        TargetRight = 0;
        TargetBottom = 0;
    }

    public void MarkApplied(bool enableInput)
    {
        HasAppliedState = true;
        InputEnabled = enableInput;
    }

    public void Reset()
    {
        WorkerWindowHandle = 0;
        DesktopViewWindowHandle = 0;
        InteractiveWorkerRaised = false;
        HasAppliedState = false;
        InputEnabled = false;
        ClearTargetRectangle();
    }
}
