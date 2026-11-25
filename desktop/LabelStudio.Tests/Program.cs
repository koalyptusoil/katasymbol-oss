using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelStudio;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
if (SelfTests.Run() != 0) return 1;
static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static void Frame(Window window)
{
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
}

static void Click(MainWindow window, string text)
{
    var button = window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, text));
    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Frame(window);
}

var commandModifier = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
var window = new MainWindow();
window.Show();
Frame(window);
var canvas = window.GetVisualDescendants().OfType<LabelCanvas>().Single();
var initial = canvas.Document.Serialize();
Click(window, "+ Text");
Check(canvas.Document.Objects.Count == 2, "Add text");
Click(window, "Undo");
Check(canvas.Document.Serialize() == initial, "Undo restores original document");
Click(window, "Redo");
Check(canvas.Document.Objects.Count == 2, "Redo restores added object");
Click(window, "Delete");
Check(canvas.Document.Objects.Count == 1, "Delete selected object");
Click(window, "+ Border");
Check(canvas.Document.Objects[0].Kind == "border", "Border inserted behind text");
Click(window, "Bring forward");
Check(canvas.Document.Objects[1].Kind == "border", "Layer order");
Click(window, "Undo");
Click(window, "Undo");
Check(canvas.Document.Objects.Count == 1, "Undo layers");
Console.WriteLine("PASS add, delete, layer order, undo, redo");
// Shrink a text box and drag it through the real pointer-event path.
var o = canvas.Document.Objects[0];
o.Width = 20;
o.Height = 6;
o.X = 2;
o.Y = 2;
canvas.Selected = 0;
canvas.UpdateRaster();
Frame(window);
var scale = Math.Min((canvas.Bounds.Width - 96) / canvas.Document.LengthMm, (canvas.Bounds.Height - 110) / canvas.Document.HeightMm);
var paperX = (canvas.Bounds.Width - canvas.Document.LengthMm * scale) / 2;
var paperY = (canvas.Bounds.Height - canvas.Document.HeightMm * scale) / 2;
Point At(double x, double y) => canvas.TranslatePoint(new Point(paperX + x * scale, paperY + y * scale), window)!.Value;
window.MouseDown(At(10, 5), MouseButton.Left);
window.MouseMove(At(13, 6), RawInputModifiers.LeftMouseButton);
window.MouseUp(At(13, 6), MouseButton.Left);
Frame(window);
Check(o.X == 5 && o.Y == 3, $"Drag positions: {o.X}, {o.Y}");
window.MouseDown(At(25, 9), MouseButton.Left);
window.MouseMove(At(28, 10), RawInputModifiers.LeftMouseButton);
window.MouseUp(At(28, 10), MouseButton.Left);
Frame(window);
Check(o.Width == 23 && o.Height == 7, $"Resize: {o.Width}, {o.Height}");
window.MouseDown(At(15, 6), MouseButton.Left);
window.MouseMove(At(80, 30), RawInputModifiers.LeftMouseButton);
window.MouseUp(At(80, 30), MouseButton.Left);
Frame(window);
Check(o.X + o.Width <= canvas.Document.LengthMm && o.Y + o.Height <= canvas.Document.HeightMm, "Drag clamps to label");
Console.WriteLine("PASS pointer selection, movement, resize, bounds clamping");
var before = canvas.Raster!.Pixels.ToArray();
var dots = window.GetVisualDescendants().OfType<CheckBox>().First(c => Equals(c.Content, "Show printer dots"));
dots.IsChecked = true;
Frame(window);
Check(canvas.ShowDots && canvas.Raster.Pixels.SequenceEqual(before), "Raw dot mode unchanged raster");
dots.IsChecked = false;
Frame(window);
Check(!canvas.ShowDots && canvas.Raster.Pixels.SequenceEqual(before), "Smooth mode unchanged raster");
Console.WriteLine("PASS preview modes preserve printable bytes");
// Capture a fresh window so inspector values and the canvas describe the same design.
var clean = new MainWindow();
clean.Show();
Frame(clean);
var screenshot = Path.GetFullPath("out/csharp-editor-filtered.png");
Directory.CreateDirectory(Path.GetDirectoryName(screenshot)!);
using (var image = new RenderTargetBitmap(new PixelSize((int)clean.Bounds.Width, (int)clean.Bounds.Height)))
{
    image.Render(clean);
    image.Save(screenshot);
}

// Verify expanded print options and the print button still fit at the minimum window size.
clean.Width = 1020;
clean.Height = 720;
clean.GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
Frame(clean);
var print = clean.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Print label"));
var printBottom = print.TranslatePoint(new Point(0, print.Bounds.Height), clean)!.Value.Y;
Check(printBottom <= clean.Bounds.Height, $"Print button visible at minimum size: {printBottom} vs {clean.Bounds.Height}");
Console.WriteLine("PASS compact window keeps expanded print settings and print button visible");
Console.WriteLine("PASS rendered editor screenshot: " + screenshot);
Console.WriteLine("All interaction checks passed. No hardware jobs sent.");
// Edit through actual canvas pointer and keyboard events, with no sidebar text field.
var typing = new MainWindow();
typing.Show();
Frame(typing);
var tc = typing.GetVisualDescendants().OfType<LabelCanvas>().Single();
Point LabelPoint(LabelCanvas control, Window host, double x, double y)
{
    double s = Math.Min((control.Bounds.Width - 96) / control.Document.LengthMm, (control.Bounds.Height - 110) / control.Document.HeightMm) * control.Zoom;
    return control.TranslatePoint(new Point((control.Bounds.Width - control.Document.LengthMm * s) / 2 + x * s, (control.Bounds.Height - control.Document.HeightMm * s) / 2 + y * s), host)!.Value;
}

void Tap(Window host, Point point)
{
    host.MouseDown(point, MouseButton.Left);
    host.MouseUp(point, MouseButton.Left);
    Frame(host);
}

void Key(Window host, Avalonia.Input.Key key, RawInputModifiers modifiers = RawInputModifiers.None)
{
    host.KeyPress(key, modifiers, PhysicalKey.None, null);
    host.KeyRelease(key, modifiers, PhysicalKey.None, null);
    Frame(host);
}

var center = LabelPoint(tc, typing, 20, 6);
Tap(typing, center);
Tap(typing, center);
Check(tc.IsEditingText, "Double-click opens editor on the canvas");
var input = tc.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "CanvasTextEditor");
Check(input.IsFocused && input.Bounds.Width > 100, "Inline editor is focused and occupies the label object");
Key(typing, Avalonia.Input.Key.A, commandModifier);
typing.KeyTextInput("Green tea");
Frame(typing);
Check(tc.Document.Objects[0].Text == "Green tea", $"Typing edits label text directly: doc={tc.Document.Objects[0].Text}, input={input.Text}, focused={input.IsFocused}, selected={input.SelectionStart}:{input.SelectionEnd}, editing={tc.IsEditingText}");
Key(typing, Avalonia.Input.Key.Enter);
typing.KeyTextInput("☕");
Frame(typing);
Check(tc.Document.Objects[0].Text == "Green tea\n☕", "Enter inserts a line and Unicode text survives");
Check(tc.Raster!.Pixels.SequenceEqual(Rasterizer.Render(tc.Document).Pixels), "Live edit uses the exact printable raster");
Key(typing, Avalonia.Input.Key.Z, commandModifier);
Check(tc.Document.Objects[0].Text == "Made on Linux", "Undo restores the whole inline text session");
Key(typing, Avalonia.Input.Key.Y, commandModifier);
Check(tc.Document.Objects[0].Text == "Green tea\n☕", "Redo restores inline text");
Check(!typing.GetVisualDescendants().OfType<TextBox>().Any(t => t.Text == tc.Document.Objects[0].Text && t.Name != "LayerName"), "Text content is not edited in a sidebar field");
Console.WriteLine("PASS double-click canvas typing, selection replacement, multiline Unicode, live raster, undo and redo");
// Pointer hit testing follows the native printed glyph positions.
tc.Document.Objects[0].Text = "ABCDE";
tc.Document.Objects[0].Align = "left";
tc.UpdateRaster();
tc.BeginTextEdit();
Frame(typing);
Tap(typing, LabelPoint(tc, typing, tc.Document.Objects[0].X + .05, tc.Document.Objects[0].Y + tc.Document.Objects[0].Height / 2));
typing.KeyTextInput("X");
Frame(typing);
Check(tc.Document.Objects[0].Text == "XABCDE", "Clicking the start of printed text places the caret at its start");
Key(typing, Avalonia.Input.Key.Home, RawInputModifiers.Shift);
typing.KeyTextInput("Y");
Frame(typing);
Check(tc.Document.Objects[0].Text == "YABCDE", "Shift+Home selects native text and typing replaces it");
Key(typing, Avalonia.Input.Key.Escape);
Check(!tc.IsEditingText, "Escape exits inline editing");
Console.WriteLine("PASS native glyph hit testing, caret selection, and Escape");
var layering = new MainWindow();
layering.Show();
Frame(layering);
var lc = layering.GetVisualDescendants().OfType<LabelCanvas>().Single();
Click(layering, "+ Border");
var list = layering.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "LayerList");
ListBoxItem[] Rows() => list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
ToggleButton Toggle(ListBoxItem row, string name) => row.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == name);
void ToggleClick(ToggleButton button) => Tap(layering, button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), layering)!.Value);
Check(Rows()[0].GetVisualDescendants().OfType<TextBox>().Single().Text == "Made on Linux", "Frontmost layer appears at top");
ToggleClick(Toggle(Rows()[0], "VisibilityToggle"));
Check(!lc.Document.Objects[1].Visible, "Visibility control hides text layer");
var hidden = lc.Raster!.Pixels.ToArray();
var shownDoc = lc.Document.Clone();
shownDoc.Objects[1].Visible = true;
Check(!hidden.SequenceEqual(Rasterizer.Render(shownDoc).Pixels), "Hidden layers are omitted from print raster");
ToggleClick(Toggle(Rows()[0], "VisibilityToggle"));
list.SelectedItem = Rows()[0];
Frame(layering);
ToggleClick(Toggle(Rows()[0], "LockToggle"));
Check(lc.Document.Objects[1].Locked && !lc.BeginTextEdit(), "Locked text cannot enter editing");
Click(layering, "Delete");
Check(lc.Document.Objects.Count == 2, "Locked layer cannot be deleted");
var lockedObject = lc.Document.Objects[1];
var oldX = lockedObject.X;
Tap(layering, LabelPoint(lc, layering, 20, 6));
Check(lc.Selected != 1 && lockedObject.X == oldX, "Canvas hit testing skips locked objects");
ToggleClick(Toggle(Rows()[0], "LockToggle"));
var name = Rows()[0].GetVisualDescendants().OfType<TextBox>().Single();
name.Focus();
name.SelectAll();
layering.KeyTextInput("Tea title");
Frame(layering);
Check(lc.Document.Objects[1].Name == "Tea title", "Rename layer inline in the layers panel");
Check(lc.Document.Objects[1].Text == "Made on Linux", "Renaming never changes printed text");
lc.Focus();
Frame(layering);
// Drag the frontmost text layer behind the border via the layer's type icon.
var fromIcon = Rows()[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "T");
var toRow = Rows()[1];
var fromPoint = fromIcon.TranslatePoint(new Point(fromIcon.Bounds.Width / 2, fromIcon.Bounds.Height / 2), layering)!.Value;
var toPoint = toRow.TranslatePoint(new Point(10, toRow.Bounds.Height / 2), layering)!.Value;
layering.MouseDown(fromPoint, MouseButton.Left);
layering.MouseMove(toPoint, RawInputModifiers.LeftMouseButton);
layering.MouseUp(toPoint, MouseButton.Left);
Frame(layering);
Check(lc.Document.Objects[0].Kind == "text" && lc.Selected == 0, "Drag reorders layer stack and preserves selection");
Click(layering, "Undo");
Check(lc.Document.Objects[1].Kind == "text", "Layer order is undoable");
var saved = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".labelstudio");
try
{
    var portable = lc.Document.Clone();
    portable.Objects[1].Visible = false;
    portable.Objects[1].Locked = true;
    portable.Save(saved);
    var loaded = LabelDocument.Load(saved);
    Check(loaded.Objects[1].Name == "Tea title" && !loaded.Objects[1].Visible && loaded.Objects[1].Locked, "Layer metadata survives save/load");
}
finally
{
    File.Delete(saved);
}

Console.WriteLine("PASS layers selection, visibility/print exclusion, locking, rename, drag reorder, undo, and persistence");
// Capture the actual inline editing display with two layers.
var demo = new MainWindow();
demo.Show();
Frame(demo);
Click(demo, "+ Border");
var dc = demo.GetVisualDescendants().OfType<LabelCanvas>().Single();
dc.Selected = 1;
Tap(demo, LabelPoint(dc, demo, 20, 6));
Tap(demo, LabelPoint(dc, demo, 20, 6));
Check(dc.IsEditingText, "Demo double-click editor");
Key(demo, Avalonia.Input.Key.A, commandModifier);
demo.KeyTextInput("Green tea");
Frame(demo);
var shot = Path.GetFullPath("out/inline-text-and-layers.png");
using (var image = new RenderTargetBitmap(new PixelSize((int)demo.Bounds.Width, (int)demo.Bounds.Height)))
{
    image.Render(demo);
    image.Save(shot);
}

Console.WriteLine("PASS inline editor screenshot: " + shot);
Console.WriteLine("All editor checks passed. No hardware jobs sent.");
// Auto length updates the physical paper and inline input bounds during a live typing session.
var automatic = new MainWindow();
automatic.Show();
Frame(automatic);
var ac = automatic.GetVisualDescendants().OfType<LabelCanvas>().Single();
Click(automatic, "+ Border");
ac.Selected = 1;
var fit = automatic.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "AutoLength");
fit.IsChecked = true;
Frame(automatic);
Check(ac.Document.AutoLength && ac.Document.Objects[0].Width == ac.Document.LengthMm - 1, "Enable auto length and stretch frame");
var automaticBeforeTyping = ac.Document.Serialize();
Check(ac.BeginTextEdit(selectAll: true), "Start inline editing with auto length");
automatic.KeyTextInput("A much longer title for the tea shelf");
Frame(automatic);
var largeLength = ac.Document.LengthMm;
var autoInput = ac.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "CanvasTextEditor");
Check(ac.IsEditingText && autoInput.IsFocused, "Resizing preserves focus and editing session");
Check(ac.Document.Objects[1].FontMm == 4 && largeLength > 40, "Long text grows tape at original size");
Check(ac.Raster!.Width == (int)Math.Round(largeLength * ac.Document.Profile.DotsPerMm), "Preview uses automatically computed printer dimensions");
Check(ac.Document.Objects[0].Width == largeLength - 1, "Frame follows live growth");
var autoScale = Math.Min((ac.Bounds.Width - 96) / ac.Document.LengthMm, (ac.Bounds.Height - 110) / ac.Document.HeightMm);
Check(Math.Abs(autoInput.Bounds.Width - ac.Document.Objects[1].Width * autoScale) < 1, "Inline overlay is rearranged to match new paper scale");
Key(automatic, Avalonia.Input.Key.A, commandModifier);
automatic.KeyTextInput("Tea");
Frame(automatic);
Check(ac.Document.LengthMm < largeLength && ac.IsEditingText && autoInput.IsFocused, "Shorter text shrinks tape without leaving editor");
var automaticAfterTyping = ac.Document.Serialize();
Key(automatic, Avalonia.Input.Key.Z, commandModifier);
Check(ac.Document.Serialize() == automaticBeforeTyping && !ac.IsEditingText, "Undo restores text, automatic length, and frame together");
Key(automatic, Avalonia.Input.Key.Y, commandModifier);
Check(ac.Document.Serialize() == automaticAfterTyping, "Redo restores automatic layout");
fit.IsChecked = false;
Frame(automatic);
Check(!ac.Document.AutoLength, "Disable automatic length");
var fixedLength = ac.Document.LengthMm;
Check(ac.BeginTextEdit(selectAll: true), "Edit fixed label");
automatic.KeyTextInput("A longer line with fixed tape length");
Frame(automatic);
Check(ac.Document.LengthMm == fixedLength, "Manual mode preserves chosen label length");
Key(automatic, Avalonia.Input.Key.Escape);
fit.IsChecked = true;
Frame(automatic);
Check(ac.Document.LengthMm > fixedLength, "Reenable auto length fits existing text");
var autoShot = Path.GetFullPath("out/automatic-label-length.png");
using (var image = new RenderTargetBitmap(new PixelSize((int)automatic.Bounds.Width, (int)automatic.Bounds.Height)))
{
    image.Render(automatic);
    image.Save(autoShot);
}

Console.WriteLine("PASS automatic label length, live focus/overlay/print size, shrinking/growing frame, undo/redo and fixed mode");
Console.WriteLine("PASS automatic length screenshot: " + autoShot);

return 0;
