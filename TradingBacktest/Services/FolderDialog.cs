using System;
using System.IO;

namespace TradingBacktest.Services
{
    /// <summary>
    /// Native "select a folder" dialog, behind one method so the rest of the app does not
    /// care which dialog it is.
    ///
    /// This uses <see cref="System.Windows.Forms.FolderBrowserDialog"/>, which on Windows
    /// Vista and later is itself the modern shell dialog - address bar, breadcrumbs, search
    /// box and all - so nothing is given up visually by letting the framework own the call.
    ///
    /// The obvious alternative, declaring IFileDialog by hand, was tried first and
    /// crashed the process with an access violation on the first call that takes a string.
    /// Two traps are worth remembering if anyone retries it:
    ///  * in a [ComImport] interface a bare "string" parameter marshals as a BSTR, not an
    ///    LPCWSTR, so every path argument needs [MarshalAs(UnmanagedType.LPWStr)];
    ///  * GetResult returns a PIDLIST, not a string - it has to go through
    ///    SHGetPathFromIDList and be released with ILFree, never read as text and never
    ///    CoTaskMemFree'd.
    /// Letting the framework own the call needs none of that, and picking a folder is the
    /// one interaction a user must never be able to crash the app with.
    /// </summary>
    public static class FolderDialog
    {
        /// <summary>
        /// Shows the dialog. Returns the chosen path, or null when the user cancelled.
        /// </summary>
        public static string Browse(string initialDirectory)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select the folder with the exported history files";
                dialog.ShowNewFolderButton = false;

                // A SelectedPath that does not exist makes the dialog throw, so only pass it
                // through when the folder is really there.
                if (!string.IsNullOrEmpty(initialDirectory))
                {
                    try
                    {
                        if (Directory.Exists(initialDirectory))
                        {
                            dialog.SelectedPath = initialDirectory;
                        }
                    }
                    catch (Exception)
                    {
                        // Fall back to the default start folder.
                    }
                }

                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? dialog.SelectedPath
                    : null;
            }
        }

    }
}
