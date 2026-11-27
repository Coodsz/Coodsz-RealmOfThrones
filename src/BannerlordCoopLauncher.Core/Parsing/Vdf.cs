namespace BannerlordCoopLauncher.Core.Parsing;

public sealed class VdfException : Exception
{
    public VdfException(string message) : base(message) { }
}

public sealed class VdfNode
{
    public VdfNode(string key, string? value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }
    public string? Value { get; }
    public List<VdfNode> Children { get; } = new();

    public VdfNode? FindFirst(string key)
    {
        if (string.Equals(Key, key, StringComparison.OrdinalIgnoreCase) && Value != null)
            return this;

        foreach (var child in Children)
        {
            var found = child.FindFirst(key);
            if (found != null)
                return found;
        }

        return null;
    }

    public VdfNode? Child(string key)
    {
        foreach (var child in Children)
        {
            if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }
}

public static class Vdf
{
    public const int MaxCharacters = 20_000_000;
    public const int MaxDepth = 32;

    public static VdfNode Parse(string text)
    {
        if (text.Length > MaxCharacters)
            throw new VdfException("Steam manifest too large.");

        return new VdfParser(text).ParseRoot();
    }

    private sealed class VdfParser
    {
        private readonly string _text;
        private int _index;

        public VdfParser(string text) => _text = text.TrimStart('\uFEFF');

        public VdfNode ParseRoot()
        {
            Skip();
            if (End)
                throw new VdfException("Unknown Steam manifest format.");

            var key = ReadString();
            Skip();
            if (!End && Peek == '{')
            {
                var node = new VdfNode(key, null);
                ReadObject(node, 1);
                return node;
            }

            if (!End && Peek == '"')
                return new VdfNode(key, ReadString());

            throw new VdfException("Unknown Steam manifest format.");
        }

        private void ReadObject(VdfNode parent, int depth)
        {
            if (depth > MaxDepth)
                throw new VdfException("Steam manifest nesting too deep.");

            Expect('{');
            Skip();
            while (!End && Peek != '}')
            {
                var key = ReadString();
                Skip();
                if (Peek == '{')
                {
                    var child = new VdfNode(key, null);
                    ReadObject(child, depth + 1);
                    parent.Children.Add(child);
                }
                else if (Peek == '"')
                {
                    parent.Children.Add(new VdfNode(key, ReadString()));
                }
                else
                {
                    throw new VdfException("Unknown Steam manifest format.");
                }

                Skip();
            }

            Expect('}');
        }

        private string ReadString()
        {
            if (End || Peek != '"')
                throw new VdfException("Incomplete Steam manifest.");

            _index++;
            var value = new System.Text.StringBuilder();
            while (!End)
            {
                var current = _text[_index++];
                if (current == '\\')
                {
                    if (End)
                        throw new VdfException("Incomplete Steam manifest.");
                    var escaped = _text[_index++];
                    value.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => escaped
                    });
                    continue;
                }

                if (current == '"')
                    return value.ToString();

                value.Append(current);
            }

            throw new VdfException("Incomplete Steam manifest.");
        }

        private void Expect(char expected)
        {
            if (End || _text[_index] != expected)
                throw new VdfException("Unknown Steam manifest format.");
            _index++;
        }

        private void Skip()
        {
            while (!End && char.IsWhiteSpace(_text[_index]))
                _index++;
        }

        private bool End => _index >= _text.Length;
        private char Peek => End ? (char)0 : _text[_index];
    }
}
