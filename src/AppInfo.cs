using System.Reflection;

[assembly: AssemblyTitle("Breathing Room")]
[assembly: AssemblyDescription("A desk cat that helps you notice your smoking. No rules, no lectures.")]
[assembly: AssemblyProduct("Breathing Room")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Breathing Room contributors. MIT License.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

namespace BreathingRoom
{
    public static class AppInfo
    {
        public const string Name = "Breathing Room";
        public const string Version = "1.0.0";

        // The GitHub repository: About links to it, and the updater reads its releases.
        public const string ProjectUrl = "https://github.com/richard-nodev/breathing-room";
    }
}
