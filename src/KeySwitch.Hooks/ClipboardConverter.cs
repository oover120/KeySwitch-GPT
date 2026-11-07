using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using KeySwitch.Core;
namespace KeySwitch.Hooks;

/// <summary>Runs on the UI STA. Any unsupported format aborts before touching the clipboard.</summary>
public sealed class ClipboardConverter
{
    private bool running;
    public async Task<string?> ConvertAsync(WindowContext context, Func<WindowContext, bool> matches)
    {
        if (running || !matches(context)) return "Выделение недоступно в этом поле.";
        running = true;
        DataObject? saved = null; uint ownedSequence = 0;
        try
        {
            var original = Clipboard.GetDataObject();
            saved = new DataObject();
            if (original is not null)
            {
                foreach (var format in original.GetFormats(false))
                {
                    var value = original.GetData(format, false);
                    if (value is null) throw new InvalidOperationException("Unsupported clipboard format");
                    // Materialize delayed formats before taking clipboard ownership.
                    if (value is Stream stream)
                    { var copy = new MemoryStream(); if (stream.CanSeek) stream.Position = 0; stream.CopyTo(copy); copy.Position = 0; value = copy; }
                    else if (value is BitmapSource bitmap) { value = bitmap.Clone(); ((BitmapSource)value).Freeze(); }
                    saved.SetData(format, value, false);
                }
            }
            var before = Native.GetClipboardSequenceNumber();
            InputSender.Chord(0x43);
            for (var i = 0; i < 20 && Native.GetClipboardSequenceNumber() == before; i++) await Task.Delay(10);
            var copied = Native.GetClipboardSequenceNumber();
            if (copied == before) return "Приложение не скопировало выделение.";
            ownedSequence = copied;
            if (!matches(context) || !Clipboard.ContainsText()) return "Текстовое выделение недоступно.";
            var text = Clipboard.GetText();
            if (text.Length == 0 || text.Length > 100_000 || PasswordHeuristic.IsSensitive(text)) return "Выделение пустое, слишком длинное или похоже на пароль.";
            var converted = ConvertMixed(text);
            if (!matches(context)) return "Фокус изменился; вставка отменена.";
            Clipboard.SetText(converted); ownedSequence = Native.GetClipboardSequenceNumber();
            InputSender.Chord(0x56);
            // Give the receiving process time to materialize CF_UNICODETEXT.
            await Task.Delay(180);
            return null;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ExternalException or InvalidOperationException or NotSupportedException or IOException)
        { return "Буфер обмена занят или содержит неподдерживаемый формат. Повторите действие."; }
        finally
        {
            try
            {
                // Never overwrite clipboard changes made by the user or a clipboard manager.
                if (saved is not null && ownedSequence != 0 && Native.GetClipboardSequenceNumber() == ownedSequence)
                    Clipboard.SetDataObject(saved, true);
            }
            catch { /* Clipboard ownership may change; do not retry over newer content. */ }
            running = false;
        }
    }
    public static string ConvertMixed(string text)
    {
        // Choose source per letter-run, preserving spaces and sentence punctuation.
        return System.Text.RegularExpressions.Regex.Replace(text, @"[A-Za-z\[\]';`,.]+|[А-Яа-яЁё]+", match =>
        {
            var value = match.Value;
            if (value.Any(c => c is >= 'А' and <= 'я' or 'Ё' or 'ё')) return Layouts.Russian.ConvertTo(value, Layouts.English);
            var end = value.Length; while (end > 0 && value[end - 1] is '.' or ',' or ';' or '\'') end--;
            return Layouts.English.ConvertTo(value[..end], Layouts.Russian) + value[end..];
        });
    }
}
