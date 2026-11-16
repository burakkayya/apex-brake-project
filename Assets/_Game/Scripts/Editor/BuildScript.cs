using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ApexBrake.Editor
{
    public static class BuildScript
    {
        public const string AndroidOutput = "Build/Android/ApexBrake.apk";

        /// <summary>Build Settings'teki sahneleri geliştirme APK'sı olarak derler. Batchmode'da hata olursa çıkış kodu 1.</summary>
        public static void BuildAndroid()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AndroidOutput));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = AndroidOutput,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildScript] Android derlemesi tamam: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} sn)");
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            Debug.LogError($"[BuildScript] Android derlemesi başarısız: {summary.result}, hata sayısı {summary.totalErrors}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw new BuildFailedException("Android build failed: " + summary.result);
        }
    }
}
