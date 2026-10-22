using BrewHouse.Data;
using Xunit;

namespace BrewHouse.Tests;

public class CoffeeLotEmbeddingTests
{
    [Fact]
    public void Shared_Words_Are_Closer_Than_Unrelated_Origins()
    {
        var roast = CoffeeLotEmbedding.Compute("Terraform Roast");
        var darkRoast = CoffeeLotEmbedding.Compute("Wolverine Dark Roast");
        var garnet = CoffeeLotEmbedding.Compute("Garnet Reserve");

        Assert.Equal(CoffeeLotEmbedding.Dimensions, roast.Length);
        Assert.True(Cosine(roast, darkRoast) > Cosine(roast, garnet));
    }

    private static float Cosine(float[] left, float[] right)
    {
        float dot = 0, leftNorm = 0, rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        return dot / (MathF.Sqrt(leftNorm) * MathF.Sqrt(rightNorm));
    }
}
