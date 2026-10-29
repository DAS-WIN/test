using System.Globalization;
using System.Text;

namespace SrbMinerMonitor.Core;

public static class TerminalOutputReader
{
    public static void Pump(Stream stream, Action<string> emit)
    {
        var terminal = new Screen(emit);
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[4096]; var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int read;
        while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
        {
            var count = decoder.GetChars(bytes, 0, read, chars, 0, false);
            for (var i = 0; i < count; i++) terminal.Accept(chars[i]);
        }
        var final = decoder.GetChars([], 0, 0, chars, 0, true);
        for (var i = 0; i < final; i++) terminal.Accept(chars[i]);
        terminal.Finish();
    }
    private sealed class Screen(Action<string> emit)
    {
        private const int Width = 300, Height = 100;
        private readonly Dictionary<int, char[]> _rows = [];
        private readonly Dictionary<int, string> _emitted = [];
        private readonly StringBuilder _sequence = new();
        private int _row = 1, _column = 1, _state;
        public void Accept(char ch)
        {
            if (_state == 3) { if (ch == '\a') _state = 0; else if (ch == '\x1b') _state = 4; return; }
            if (_state == 4) { _state = ch == '\\' ? 0 : 3; return; }
            if (_state == 1)
            {
                _state = ch == '[' ? 2 : ch == ']' ? 3 : 0;
                _sequence.Clear(); return;
            }
            if (_state == 2)
            {
                if (ch is >= '@' and <= '~') { Command(ch, _sequence.ToString()); _state = 0; }
                else if (_sequence.Length < 64) _sequence.Append(ch); else _state = 0;
                return;
            }
            switch (ch)
            {
                case '\x1b': _state = 1; return;
                case '\r': _column = 1; return;
                case '\n': Finish(); Down(); return;
                case '\b': _column = Math.Max(1, _column - 1); return;
                case '\t': _column = Math.Min(Width, ((_column - 1) / 8 + 1) * 8 + 1); return;
            }
            if (char.IsControl(ch) || ch == '\ufeff') return;
            if (_column > Width) { Finish(); Down(); _column = 1; }
            if (!_rows.TryGetValue(_row, out var row)) { row = new char[Width]; Array.Fill(row, ' '); _rows[_row] = row; }
            row[_column++ - 1] = ch;
        }
        public void Finish()
        {
            if (!_rows.TryGetValue(_row, out var chars)) return;
            var text = new string(chars).TrimEnd(' ');
            if (text.Length > 0 && (!_emitted.TryGetValue(_row, out var previous) || previous != text))
            { _emitted[_row] = text; emit(text); }
        }
        private void Down()
        {
            if (_row < Height) { _row++; return; }
            Scroll();
        }
        private void Scroll()
        {
            for (var row = 1; row < Height; row++)
            {
                if (_rows.TryGetValue(row + 1, out var chars)) _rows[row] = chars; else _rows.Remove(row);
                if (_emitted.TryGetValue(row + 1, out var line)) _emitted[row] = line; else _emitted.Remove(row);
            }
            _rows.Remove(Height); _emitted.Remove(Height);
        }
        private void Move(int row, int column)
        {
            row = Math.Clamp(row, 1, Height);
            if (row != _row) Finish();
            _row = row; _column = Math.Clamp(column, 1, Width);
        }
        private void Command(char command, string sequence)
        {
            if (sequence.StartsWith('?') || sequence.StartsWith('>')) return;
            var parts = sequence.Split(';');
            int Value(int index, int fallback) => index < parts.Length && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : fallback;
            switch (command)
            {
                case 'H': case 'f': Move(Value(0, 1), Value(1, 1)); break;
                case 'd': Move(Value(0, 1), _column); break;
                case 'G': case '\x60': _column = Math.Clamp(Value(0, 1), 1, Width); break;
                case 'A': Move(_row - Math.Max(1, Value(0, 1)), _column); break;
                case 'B': Move(_row + Math.Max(1, Value(0, 1)), _column); break;
                case 'C': _column = Math.Min(Width, _column + Math.Max(1, Value(0, 1))); break;
                case 'D': _column = Math.Max(1, _column - Math.Max(1, Value(0, 1))); break;
                case 'E': Move(_row + Math.Max(1, Value(0, 1)), 1); break;
                case 'F': Move(_row - Math.Max(1, Value(0, 1)), 1); break;
                case 'J':
                    if (Value(0, 0) is 2 or 3) _rows.Clear();
                    break;
                case 'K':
                    if (_rows.TryGetValue(_row, out var row))
                    {
                        var mode = Value(0, 0);
                        var start = mode is 1 or 2 ? 0 : Math.Min(_column - 1, Width);
                        var end = mode is 0 or 2 ? Width : Math.Min(_column, Width);
                        Array.Fill(row, ' ', start, end - start);
                    }
                    break;
                case 'S':
                    Finish(); for (var i = 0; i < Math.Clamp(Value(0, 1), 1, Height); i++) Scroll();
                    break;
            }
        }
    }
}
