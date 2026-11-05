using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace EverQuestBetaUiCopier
{
    // Tests construct and render our form offscreen; no desktop input or game writes.
    internal static class WinFormsTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (CopierForm form = new CopierForm())
                {
                    Require(!Field<Button>(form, "copyButton").Enabled, "Copy starts disabled");
                    CheckBox maps = Field<CheckBox>(form, "includeMaps");
                    Require(maps.Checked, "Include New Maps is checked by default");
                    Require(maps.Text == "Include New Maps", "Maps option has the requested label");
                    SetField(form, "liveDirectory", "C:\\EverQuest\\Live");
                    SetField(form, "betaDirectory", "C:\\EverQuest\\Beta");
                    Field<TextBox>(form, "livePath").Text = "C:\\EverQuest\\Live";
                    Field<TextBox>(form, "betaPath").Text = "C:\\EverQuest\\Beta";
                    SetField(form, "profiles", new List<Profile> {
                        MakeProfile("Hotshott", "xegony", "SHM", true),
                        MakeProfile("Wizard", "xegony", "WIZ", false),
                        MakeProfile("Ranger", "xegony", "RNG", true),
                        MakeProfile("Ranger", "bristlebane", "RNG", true)
                    });
                    Invoke(form, "PopulateProfiles");
                    DataGridView grid = Field<DataGridView>(form, "profilesGrid");
                    Require(grid.Rows.Count == 4, "All discovered profiles shown");
                    Require(Convert.ToBoolean(grid.Rows[0].Cells[0].Value) && Convert.ToBoolean(grid.Rows[1].Cells[0].Value), "Unique profiles selected");
                    Require(!Convert.ToBoolean(grid.Rows[2].Cells[0].Value) && !Convert.ToBoolean(grid.Rows[3].Cells[0].Value), "Conflicting profiles unselected");
                    Require(Field<Button>(form, "copyButton").Enabled, "Valid selection enables copying");
                    Require(Field<Button>(form, "copyButton").Text == "Copy 3 files + maps to Beta", "Copy button includes the maps option");
                    maps.Checked = false;
                    Require(Field<Button>(form, "copyButton").Text == "Copy 3 files to Beta", "Unchecking maps restores UI-only copy");
                    Require(Field<Button>(form, "copyButton").Enabled, "UI-only copy remains available");
                    maps.Checked = true;
                    Require(Field<Label>(form, "result").Text.Contains("Highlighted"), "Conflict instructions visible");

                    Invoke(form, "SetSelection", true);
                    Require(!Field<Button>(form, "copyButton").Enabled, "Duplicate selection blocks copying");
                    Require(Field<Label>(form, "summary").Text.Contains("same Beta filename"), "Conflict explained");
                    Invoke(form, "SetSelection", false);
                    Require(!Field<Button>(form, "copyButton").Enabled, "Empty selection blocks copying");
                    grid.Rows[0].Cells[0].Value = true;
                    grid.Rows[1].Cells[0].Value = true;
                    grid.Rows[2].Cells[0].Value = true;
                    Require(Field<Button>(form, "copyButton").Enabled, "Selecting one of conflicting sources works");

                    if (args.Length == 1)
                    {
                        string output = Path.GetFullPath(args[0]);
                        Directory.CreateDirectory(output);
                        // An unshown Form makes descendant Visible values false. Render its
                        // same content tree on an offscreen control, without a native window.
                        using (Panel canvas = new Panel { Size = form.ClientSize, BackColor = form.BackColor, Font = form.Font, ForeColor = form.ForeColor })
                        {
                            Control content = form.Controls[0];
                            canvas.Controls.Add(content);
                            canvas.CreateControl();
                            canvas.PerformLayout();
                            Require(grid.Height > 100, "Profile list has usable height");
                            Render(canvas, Path.Combine(output, "copier-preview.png"));
                            canvas.Size = new Size(784, 683);
                            canvas.PerformLayout();
                            Require(grid.Height > 80, "Profile list fits minimum size");
                            Render(canvas, Path.Combine(output, "copier-preview-small.png"));
                        }
                    }
                }
                Console.WriteLine("PASS Windows form selection, conflict guidance, and offscreen rendering");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static Profile MakeProfile(string character, string server, string classCode, bool companion)
        {
            string stem = character + "_" + server + "_" + classCode + ".ini";
            return new Profile { Character = character, Server = server, ClassCode = classCode,
                UiSourcePath = "UI_" + stem, CompanionSourcePath = companion ? stem : null,
                DestinationUiName = "UI_" + character + "_beta_" + classCode + ".ini",
                DestinationCompanionName = character + "_beta_" + classCode + ".ini" };
        }

        private static void Render(Control control, string path)
        {
            using (Bitmap bitmap = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static void SetField(object target, string name, object value) { target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value); }
        private static void Invoke(object target, string name, params object[] values) { target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, values); }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
