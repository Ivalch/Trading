using System;
using System.Runtime.InteropServices;

namespace TradingBacktest.Services
{
    /// <summary>
    /// Native "Select Folder" dialog. WPF on .NET 4.x has no folder browser of its own and
    /// SHBrowseForFolder is deprecated, so this calls the Vista-era IFileDialog with the
    /// pick-folder option set. That gives the modern shell dialog with an address bar, a
    /// search box and a working "New folder" button.
    /// </summary>
    public static class FolderDialog
    {
        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x40000000;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        /// <summary>
        /// Shows the dialog. Returns the chosen path, or null when the user cancelled.
        /// Throws only if both the shell dialog and the fallback fail.
        /// </summary>
        public static string Browse(string initialDirectory)
        {
            try
            {
                return BrowseWithShell(initialDirectory);
            }
            catch (Exception)
            {
                // Locked-down machines without the shell COM server: use WinForms instead.
                return BrowseWithWinForms(initialDirectory);
            }
        }

        private static string BrowseWithShell(string initialDirectory)
        {
            var dialog = (IFileDialog)new FileOpenDialogRcw();
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);

                if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
                {
                    dialog.SetFolder(initialDirectory);
                }

                dialog.Show(IntPtr.Zero);

                IntPtr buffer = IntPtr.Zero;
                try
                {
                    dialog.GetResult(out buffer);
                    return buffer == IntPtr.Zero ? null : Marshal.PtrToStringUni(buffer);
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                    {
                        CoTaskMemFree(buffer);
                    }
                }
            }
            catch (COMException ex)
            {
                if (ex.HResult == ERROR_CANCELLED)
                {
                    return null;
                }

                throw;
            }
        }

        private static string BrowseWithWinForms(string initialDirectory)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
                {
                    dialog.SelectedPath = initialDirectory;
                }

                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? dialog.SelectedPath
                    : null;
            }
        }

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        /// <summary>
        /// Co-class for the file open dialog. Activating it yields an IFileDialog; the
        /// option flags are what turn it into a folder picker.
        /// </summary>
        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialogRcw
        {
        }

        [ComImport]
        [Guid("d57c7288-d4ad-4768-be02-9d969532d960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            void Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);

            void SetFileTypeIndex(uint iFileType);

            void GetFileTypeIndex(out uint piFileType);

            void Advise(IntPtr pfde, out uint pdwCookie);

            void Unadvise(uint dwCookie);

            void SetOptions(uint fos);

            void GetOptions(out uint pfos);

            void SetDefaultFolder(string pszPath);

            void SetFolder(string pszPath);

            void GetFolder(out IntPtr ppszPath);

            void GetCurrentSelection(out IntPtr ppszPath);

            void SetFileName(string pszName);

            void GetFileName(out IntPtr ppszName);

            void SetTitle(string pszTitle);

            void SetOkButtonLabel(string pszText);

            void SetFileNameLabel(string pszLabel);

            void GetResult(out IntPtr ppszPath);

            void AddPlace(IntPtr pszPath, uint fdap);

            void SetDefaultExtension(string pszDefaultExtension);

            void Close(int hr);

            void SetClientGuid(ref Guid guid);

            void ClearClientData();

            void SetFilter(IntPtr pFilter);
        }
    }
}
