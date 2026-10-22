namespace BrewHouse.Data;

/// <summary>
/// Demo embeddings for the coffee-lot similarity query. This is a local feature hash of the
/// origin text (words plus character trigrams), not a hosted model, so the lab can show a
/// real pgvector nearest-neighbour query without an API key. ADR 133.
/// </summary>
public static class CoffeeLotEmbedding
{
    public const int Dimensions = 32;

    public static float[] Compute(string origin)
    {
        var vector = new float[Dimensions];
        if (string.IsNullOrWhiteSpace(origin))
            return vector;

        var text = origin.ToLowerInvariant();
        foreach (var word in text.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries))
            Accumulate(vector, word);

        for (var i = 0; i <= text.Length - 3; i++)
            Accumulate(vector, text.Substring(i, 3));

        var norm = MathF.Sqrt(vector.Sum(value => value * value));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }

        return vector;
    }

    private static void Accumulate(float[] vector, string token)
    {
        unchecked
        {
            var hash = 17;
            foreach (var character in token)
                hash = (hash * 31) + character;
            var bucket = (int)((uint)hash % (uint)Dimensions);
            vector[bucket] += 1f;
        }
    }
}
