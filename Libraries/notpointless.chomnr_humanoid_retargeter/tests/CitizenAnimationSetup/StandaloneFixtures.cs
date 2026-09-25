#if CITIZEN_SETUP_STANDALONE
namespace HumanoidRetargeter.Tests.Skeleton;

/// <summary>Standalone runs share the dev suite's rig fixtures (see CitizenAnimationSetup.csproj).</summary>
public static class SkeletonTests
{
    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);
}
#endif
