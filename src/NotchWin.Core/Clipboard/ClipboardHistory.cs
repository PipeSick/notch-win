using System.Security.Cryptography;

namespace NotchWin.Core.Clipboard;

public enum ClipboardItemKind
{
    Text,
    Image,
}

public sealed class ClipboardEntry
{
    public Guid Id { get; } = Guid.NewGuid();

    public required ClipboardItemKind Kind { get; init; }

    public string? Text { get; set; }

    /// <summary>PNG bytes of a copied image.</summary>
    public byte[]? ImagePng { get; init; }

    public DateTimeOffset At { get; set; }

    public bool Pinned { get; set; }
}

/// <summary>
/// In-memory clipboard history: newest first, pinned entries always on top, a size cap on unpinned ones.
/// It is deliberately never written to disk, since the clipboard often holds passwords and tokens.
/// </summary>
public sealed class ClipboardHistory(int capacity = 50)
{
    private readonly List<ClipboardEntry> _items = [];

    public event Action? Changed;

    public int Count => _items.Count;

    public void AddText(string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var existing = _items.FirstOrDefault(i => i.Kind == ClipboardItemKind.Text && i.Text == text);
        if (existing is not null)
        {
            _items.Remove(existing);
            existing.At = now;
            _items.Insert(0, existing);
        }
        else
        {
            _items.Insert(0, new ClipboardEntry { Kind = ClipboardItemKind.Text, Text = text, At = now });
        }

        TrimAndNotify();
    }

    public void AddImage(byte[] png, DateTimeOffset now)
    {
        if (png.Length == 0)
        {
            return;
        }

        var hash = SHA256.HashData(png);
        var existing = _items.FirstOrDefault(i => i.ImagePng is { } other && SHA256.HashData(other).AsSpan().SequenceEqual(hash));
        if (existing is not null)
        {
            _items.Remove(existing);
            existing.At = now;
            _items.Insert(0, existing);
        }
        else
        {
            _items.Insert(0, new ClipboardEntry { Kind = ClipboardItemKind.Image, ImagePng = png, At = now });
        }

        TrimAndNotify();
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(i => i.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    public void TogglePin(Guid id)
    {
        if (_items.FirstOrDefault(i => i.Id == id) is { } item)
        {
            item.Pinned = !item.Pinned;
            Changed?.Invoke();
        }
    }

    public void UpdateText(Guid id, string text)
    {
        if (_items.FirstOrDefault(i => i.Id == id && i.Kind == ClipboardItemKind.Text) is { } item && !string.IsNullOrWhiteSpace(text))
        {
            item.Text = text;
            Changed?.Invoke();
        }
    }

    /// <summary>Removes everything except pinned entries.</summary>
    public void Clear()
    {
        if (_items.RemoveAll(i => !i.Pinned) > 0)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Pinned entries first, then the rest, each group newest first. A query keeps only text entries containing it.</summary>
    public IReadOnlyList<ClipboardEntry> View(string? query = null)
    {
        IEnumerable<ClipboardEntry> source = _items;
        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(i => i.Text?.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        }

        return source.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.At).ToList();
    }

    private void TrimAndNotify()
    {
        var unpinned = _items.Where(i => !i.Pinned).ToList();
        foreach (var extra in unpinned.Skip(capacity))
        {
            _items.Remove(extra);
        }

        Changed?.Invoke();
    }
}
