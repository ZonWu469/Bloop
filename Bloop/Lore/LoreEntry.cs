namespace Bloop.Lore
{
    public record LoreEntry(
        string Title,
        string Author,
        string Content,
        string PortalHint,
        int    SanityDelta
    );
}
