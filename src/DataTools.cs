using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace BreathingRoom
{
    // The user's own data: export it, look at it, or wipe it.
    public static class DataTools
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Field(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string Csv(AppData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("timestamp,day,time,app,added_later,decided,wanted,taste,moment");
            foreach (SmokeEvent s in data.Smokes.OrderBy(x => x.At))
            {
                DateTime wall = s.Wall;
                sb.Append(Field(wall.ToString("yyyy-MM-ddTHH:mm:ss", Inv))).Append(',')
                  .Append(Stats.DayStart(wall).ToString("yyyy-MM-dd", Inv)).Append(',')
                  .Append(wall.ToString("HH:mm", Inv)).Append(',')
                  .Append(Field(s.App)).Append(',')
                  .Append(s.Retro ? "yes" : "no").Append(',')
                  .Append(Field(s.Decided)).Append(',')
                  .Append(Field(s.Want)).Append(',')
                  .Append(Field(s.Taste)).Append(',')
                  .Append(Field(s.Moment)).AppendLine();
            }
            return sb.ToString();
        }

        public static void Export(Window owner)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "breathing-room-history-" + DateTime.Now.ToString("yyyy-MM-dd", Inv) + ".csv",
                Filter = "CSV file (*.csv)|*.csv",
                DefaultExt = ".csv"
            };
            if (dialog.ShowDialog(owner) != true) return;
            try
            {
                // With a byte-order mark so Excel opens it as UTF-8.
                File.WriteAllText(dialog.FileName, Csv(Store.Data), new UTF8Encoding(true));
                MessageBox.Show(owner, "Saved " + Store.Data.Smokes.Count + " entries.", "Breathing Room", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Store.Log("Export failed: " + ex.Message);
                MessageBox.Show(owner, "Couldn't save the file: " + ex.Message, "Breathing Room", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public static void OpenFolder(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex)
            {
                Store.Log("Open folder failed: " + ex.Message);
            }
        }

        public static bool DeleteHistory(Window owner)
        {
            var answer = MessageBox.Show(owner,
                "Delete every logged cigarette, answer and game score on this PC?\n\nYour settings stay. This can't be undone.",
                "Delete my history", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return false;
            Store.ClearHistory();
            return true;
        }
    }
}
