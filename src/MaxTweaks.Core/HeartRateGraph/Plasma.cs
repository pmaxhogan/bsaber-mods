using System;

namespace MaxTweaks.Core.HeartRateGraph;

/// <summary>
/// The "plasma" colour ramp (dark blue, purple, magenta, orange, yellow) as Grafana and matplotlib
/// draw it: the 256 sRGB colours of d3-scale-chromatic's interpolatePlasma, 0 = lowest value.
/// </summary>
public static class Plasma
{
    public const int Count = 256;

    // d3-scale-chromatic src/sequential-multi/viridis.js, "plasma": 6 hex digits per colour.
    private const string Table =
        "0d088710078813078916078a19068c1b068d1d068e20068f2206902406912605912805922a05932c05942e05952f0596" +
        "31059733059735049837049938049a3a049a3c049b3e049c3f049c41049d43039e44039e46039f48039f4903a04b03a1" +
        "4c02a14e02a25002a25102a35302a35502a45601a45801a45901a55b01a55c01a65e01a66001a66100a76300a76400a7" +
        "6600a76700a86900a86a00a86c00a86e00a86f00a87100a87201a87401a87501a87701a87801a87a02a87b02a87d03a8" +
        "7e03a88004a88104a78305a78405a78606a68707a68808a68a09a58b0aa58d0ba58e0ca48f0da4910ea3920fa39410a2" +
        "9511a19613a19814a099159f9a169f9c179e9d189d9e199da01a9ca11b9ba21d9aa31e9aa51f99a62098a72197a82296" +
        "aa2395ab2494ac2694ad2793ae2892b02991b12a90b22b8fb32c8eb42e8db52f8cb6308bb7318ab83289ba3388bb3488" +
        "bc3587bd3786be3885bf3984c03a83c13b82c23c81c33d80c43e7fc5407ec6417dc7427cc8437bc9447aca457acb4679" +
        "cc4778cc4977cd4a76ce4b75cf4c74d04d73d14e72d24f71d35171d45270d5536fd5546ed6556dd7566cd8576bd9586a" +
        "da5a6ada5b69db5c68dc5d67dd5e66de5f65de6164df6263e06363e16462e26561e26660e3685fe4695ee56a5de56b5d" +
        "e66c5ce76e5be76f5ae87059e97158e97257ea7457eb7556eb7655ec7754ed7953ed7a52ee7b51ef7c51ef7e50f07f4f" +
        "f0804ef1814df1834cf2844bf3854bf3874af48849f48948f58b47f58c46f68d45f68f44f79044f79143f79342f89441" +
        "f89540f9973ff9983ef99a3efa9b3dfa9c3cfa9e3bfb9f3afba139fba238fca338fca537fca636fca835fca934fdab33" +
        "fdac33fdae32fdaf31fdb130fdb22ffdb42ffdb52efeb72dfeb82cfeba2cfebb2bfebd2afebe2afec029fdc229fdc328" +
        "fdc527fdc627fdc827fdca26fdcb26fccd25fcce25fcd025fcd225fbd324fbd524fbd724fad824fada24f9dc24f9dd25" +
        "f8df25f8e125f7e225f7e425f6e626f6e826f5e926f5eb27f4ed27f3ee27f3f027f2f227f1f426f1f525f0f724f0f921";

    private static readonly byte[] Colours = Parse();

    /// <summary>Colour at <paramref name="t"/> in 0..1 (clamped; NaN reads as 0), linear between table entries.</summary>
    public static void Rgb(double t, out byte r, out byte g, out byte b)
    {
        if (!(t > 0))
        {
            t = 0;
        }
        else if (t > 1)
        {
            t = 1;
        }

        var position = t * (Count - 1);
        var index = (int)position;
        if (index >= Count - 1)
        {
            index = Count - 2;
        }

        var fraction = position - index;
        var at = index * 3;
        r = Mix(Colours[at], Colours[at + 3], fraction);
        g = Mix(Colours[at + 1], Colours[at + 4], fraction);
        b = Mix(Colours[at + 2], Colours[at + 5], fraction);
    }

    /// <summary>
    /// The ramp as <paramref name="size"/> opaque RGBA32 pixels, first pixel = lowest value: the
    /// lookup texture the graph samples with the normalised heart rate.
    /// </summary>
    public static byte[] Lut(int size)
    {
        if (size < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        var pixels = new byte[size * 4];
        for (var i = 0; i < size; i++)
        {
            Rgb(i / (double)(size - 1), out var r, out var g, out var b);
            pixels[i * 4] = r;
            pixels[(i * 4) + 1] = g;
            pixels[(i * 4) + 2] = b;
            pixels[(i * 4) + 3] = 255;
        }

        return pixels;
    }

    private static byte Mix(byte from, byte to, double fraction) => (byte)Math.Round(from + ((to - from) * fraction));

    private static byte[] Parse()
    {
        var bytes = new byte[Count * 3];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(Table.Substring(i * 2, 2), 16);
        }

        return bytes;
    }
}
