namespace SteamSync.Worker.Application.Steam;

internal static class SyncPipelineHelper
{
    public const int AppIdChunkSize = 100;

    public static IEnumerable<int[]> ChunkDistinctAppIds(IEnumerable<int> appIds, int chunkSize = AppIdChunkSize)
    {
        var distinct = appIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        for (var i = 0; i < distinct.Length; i += chunkSize)
        {
            var length = Math.Min(chunkSize, distinct.Length - i);
            var chunk = new int[length];
            Array.Copy(distinct, i, chunk, 0, length);
            yield return chunk;
        }
    }

    public static List<int> ParseSteamAppIds(IEnumerable<string?> steamAppIds)
    {
        var result = new List<int>();
        foreach (var raw in steamAppIds)
        {
            if (int.TryParse(raw, out var appId) && appId > 0)
            {
                result.Add(appId);
            }
        }

        return result;
    }
}
