using CommunityToolkit.Maui.Alerts;
using OpenUtauMobile.Views.Controls;
using Microsoft.Maui.Controls;
using OpenUtau.Audio;
using OpenUtauMobile.Utils.Permission;
using CommunityToolkit.Maui.Views;
using OpenUtauMobile.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenUtauMobile.Resources.Strings;
using SkiaSharp;
using Serilog;
using OpenUtau.Core;

namespace OpenUtauMobile.Utils
{
    public static class ObjectProvider
    {
        public static IExternalStorageService? ExternalStorageService { get; private set; }
        public static IAudioOutput? AudioOutput { get; private set; }
        public static Random Random { get; } = new Random();
        public static IAppLifeCycleHelper AppLifeCycleHelper { get; set; } = null!;
        public static SKTypeface NotoSansCJKscRegularTypeface { get; set; } = null!;

        public static void Initialize()
        {
#if ANDROID
            ExternalStorageService = new OpenUtauMobile.Platforms.Android.Utils.Permission.ExternalStorageService();
            AudioOutput = new OpenUtauMobile.Platforms.Android.Utils.Audio.AudioTrackOutput();
            AppLifeCycleHelper = new OpenUtauMobile.Platforms.Android.Utils.AndroidAppLifeCycleHelper();
#elif IOS
            ExternalStorageService = new OpenUtauMobile.Platforms.iOS.Utils.Permission.ExternalStorageService();
            AudioOutput = new OpenUtau.Audio.DummyAudioOutput();
            AppLifeCycleHelper = new OpenUtauMobile.Platforms.iOS.Utils.iOSAppLifeCycleHelper();
#elif WINDOWS
            ExternalStorageService = new OpenUtauMobile.Platforms.Windows.Utils.Permission.ExternalStorageService();
            AudioOutput = new OpenUtau.Audio.NAudioOutput();
            AppLifeCycleHelper = new OpenUtauMobile.Platforms.Windows.Utils.WindowsAppLifeCycleHelper();
#else
            throw new NotSupportedException("Unsupported platform");
#endif
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("NotoSansCJKsc-Regular.otf").Result;
                NotoSansCJKscRegularTypeface = SKTypeface.FromStream(stream);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "无法加载NotoSansCJKsc字体，使用默认字体代替。");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("字体加载失败，使用默认字体代替。", ex));
                NotoSansCJKscRegularTypeface = SKTypeface.Default;
            }
        }

        public static async Task<string> PickFile(string[] types, ContentPage context)
        {
            if (ExternalStorageService == null)
            {
                throw new InvalidOperationException("ExternalStorageService is not initialized. Call ObjectProvider.Initialize() first.");
            }
#if IOS
            try
            {
                // ========== 步骤 1 ==========
                await Toast.Make("🔵 开始选择文件...", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();

                // 不限制文件类型，让用户可以选择任何文件
                var options = new PickOptions
                {
                    PickerTitle = AppResources.SelectFileToast
                };

                // ========== 步骤 2 ==========
                await Toast.Make("🔵 文件选择器已打开", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();

                var result = await FilePicker.Default.PickAsync(options);

                // ========== 步骤 3 ==========
                if (result == null)
                {
                    await Toast.Make("⛔ 未选择文件", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                    return string.Empty;
                }

                await Toast.Make($"✅ 已选择: {Path.GetFileName(result.FullPath)}", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();

                // ========== 步骤 4：检查文件类型 ==========
                bool typeMatched = false;
                foreach (string type in types)
                {
                    if (result.FullPath.EndsWith(type, StringComparison.OrdinalIgnoreCase))
                    {
                        typeMatched = true;
                        break;
                    }
                }
                if (!typeMatched)
                {
                    string stringBuilder = string.Format(AppResources.WrongFileTypeToast, string.Join("，*", types));
                    await Toast.Make(stringBuilder, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                    return string.Empty;
                }

                // ========== 步骤 5：准备复制文件 ==========
                await Toast.Make("📂 正在复制文件...", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();

                string fileName = Path.GetFileName(result.FullPath);
                string importDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Import");
                Directory.CreateDirectory(importDir);

                string destPath = Path.Combine(importDir, fileName);

                // 如果文件已存在，添加时间戳避免冲突
                if (File.Exists(destPath))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    destPath = Path.Combine(importDir, $"{nameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
                }

                // ========== 步骤 6：使用超时机制复制文件 ==========
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    using (var sourceStream = await result.OpenReadAsync())
                    using (var destStream = File.Create(destPath))
                    {
                        await sourceStream.CopyToAsync(destStream, cts.Token);
                    }

                    await Toast.Make($"✅ 文件导入成功: {Path.GetFileName(destPath)}", CommunityToolkit.Maui.Core.ToastDuration.Long, 16).Show();
                    return destPath;
                }
                catch (OperationCanceledException)
                {
                    await Toast.Make("⏰ 文件复制超时 (30秒)，请重试", CommunityToolkit.Maui.Core.ToastDuration.Long, 16).Show();
                    return string.Empty;
                }
                catch (Exception copyEx)
                {
                    await Toast.Make($"❌ 复制失败: {copyEx.Message}", CommunityToolkit.Maui.Core.ToastDuration.Long, 16).Show();
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                await Toast.Make($"❌ 错误: {ex.Message}", CommunityToolkit.Maui.Core.ToastDuration.Long, 16).Show();
                return string.Empty;
            }
#else
            if (await RequestStoragePermissionAsync())
            {
#if !WINDOWS
                await Toast.Make(AppResources.SelectFileToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
#endif
                var filePickPopup = new FilePickerPopup(types);
                object? result = await context.ShowPopupAsync(filePickPopup);
                if (result is string selectedPath)
                {
                    if (string.IsNullOrEmpty(selectedPath))
                    {
                        return string.Empty;
                    }
                    foreach (string type in types)
                    {
                        if (selectedPath.EndsWith(type, StringComparison.OrdinalIgnoreCase))
                        {
                            return selectedPath;
                        }
                    }
                    string stringBuilder = string.Format(AppResources.WrongFileTypeToast, string.Join("，*", types));
                    await Toast.Make(stringBuilder, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                    return string.Empty;
                }
                return string.Empty;
            }
            else
            {
                await Toast.Make(AppResources.StoragePermissionDeniedToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                return string.Empty;
            }
#endif
        }

        public static async Task<string> SaveFile(string[] types, ContentPage context, string initialDirectory = "", string initialFileName = "")
        {
            if (ExternalStorageService == null)
            {
                throw new InvalidOperationException("ExternalStorageService is not initialized. Call ObjectProvider.Initialize() first.");
            }
#if IOS
            try
            {
                string projectsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects");
                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }

                string fileName = string.IsNullOrEmpty(initialFileName) ? $"project_{DateTime.Now:yyyyMMddHHmmss}" : initialFileName;
                if (types.Length > 0 && !fileName.EndsWith(types[0], StringComparison.OrdinalIgnoreCase))
                {
                    fileName += types[0];
                }

                string filePath = Path.Combine(projectsDir, fileName);

                int counter = 1;
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                while (File.Exists(filePath))
                {
                    filePath = Path.Combine(projectsDir, $"{baseName}_{counter}{ext}");
                    counter++;
                }

                Log.Information($"iOS: Save file path: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "iOS SaveFile failed");
                return string.Empty;
            }
#else
            if (await RequestStoragePermissionAsync())
            {
#if !WINDOWS
                await Toast.Make(AppResources.SelectSaveLocationToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
#endif
                var fileSaverPopup = new FileSaverPopup(types, initialDirectory, initialFileName);
                object? result = await context.ShowPopupAsync(fileSaverPopup);
                if (result is string filePath)
                {
                    if (string.IsNullOrEmpty(filePath))
                    {
                        return string.Empty;
                    }
                    return filePath;
                }
                return string.Empty;
            }
            else
            {
                await Toast.Make(AppResources.StoragePermissionDeniedToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                return string.Empty;
            }
#endif
        }

        private static async Task<bool> RequestStoragePermissionAsync()
        {
            if (ExternalStorageService == null)
            {
                throw new InvalidOperationException("ExternalStorageService is not initialized. Call ObjectProvider.Initialize() first.");
            }
            if (!await ExternalStorageService.HasManageExternalStoragePermissionAsync())
            {
                ExternalStorageService.RequestManageExternalStoragePermission();
            }
            return true;
        }
    }
}
