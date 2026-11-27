namespace BannerlordCoopLauncher.Core.Updates;

public sealed record ReleaseAsset(string Name, string DownloadUrl, string? Digest = null);

public sealed record ReleaseOffer(string Message, string Tag, IReadOnlyList<ReleaseAsset> Assets, string Repo);
