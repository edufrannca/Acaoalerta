namespace AlertaEmAcao.Modulos.Comum;

/// <summary>Ponto geográfico WGS84 (EPSG:4326).</summary>
public readonly record struct PontoGeo
{
    public double Latitude { get; }
    public double Longitude { get; }

    public PontoGeo(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new ErroDominio("coordenada_invalida", "Latitude fora do intervalo [-90, 90].");
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new ErroDominio("coordenada_invalida", "Longitude fora do intervalo [-180, 180].");
        Latitude = latitude;
        Longitude = longitude;
    }

    public static bool TentarCriar(double? lat, double? lon, out PontoGeo ponto)
    {
        ponto = default;
        if (lat is null || lon is null) return false;
        if (double.IsNaN(lat.Value) || lat is < -90 or > 90 || double.IsNaN(lon.Value) || lon is < -180 or > 180) return false;
        ponto = new PontoGeo(lat.Value, lon.Value);
        return true;
    }

    /// <summary>Distância de grande círculo (Haversine), em metros.</summary>
    public double DistanciaMetros(PontoGeo outro)
    {
        const double raioTerra = 6_371_008.8;
        static double Rad(double g) => g * Math.PI / 180d;
        var dLat = Rad(outro.Latitude - Latitude);
        var dLon = Rad(outro.Longitude - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(Latitude)) * Math.Cos(Rad(outro.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * raioTerra * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// Arredonda o ponto para uma grade aproximada (padrão ~0,005° ≈ 550 m) para exibição pública
    /// sem revelar a localização precisa de quem pediu ajuda.
    /// </summary>
    public PontoGeo Aproximar(double passoGraus = 0.005) =>
        new(Math.Round(Latitude / passoGraus) * passoGraus, Math.Round(Longitude / passoGraus) * passoGraus);
}

/// <summary>
/// MultiPolígono simples em coordenadas [lon, lat] (ordem GeoJSON).
/// Cada polígono: anel externo seguido de buracos.
/// </summary>
public sealed class MultiPoligonoGeo
{
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<double[]>>> Poligonos { get; }
    public (double MinLon, double MinLat, double MaxLon, double MaxLat) Envelope { get; }

    public MultiPoligonoGeo(IReadOnlyList<IReadOnlyList<IReadOnlyList<double[]>>> poligonos)
    {
        if (poligonos.Count == 0) throw new ErroDominio("geometria_invalida", "Multipolígono vazio.");
        foreach (var poligono in poligonos)
        {
            if (poligono.Count == 0 || poligono[0].Count < 4)
                throw new ErroDominio("geometria_invalida", "Anel externo precisa de ao menos 4 vértices (fechado).");
        }
        Poligonos = poligonos;
        double minLon = double.MaxValue, minLat = double.MaxValue, maxLon = double.MinValue, maxLat = double.MinValue;
        foreach (var v in poligonos.SelectMany(p => p[0]))
        {
            minLon = Math.Min(minLon, v[0]); maxLon = Math.Max(maxLon, v[0]);
            minLat = Math.Min(minLat, v[1]); maxLat = Math.Max(maxLat, v[1]);
        }
        Envelope = (minLon, minLat, maxLon, maxLat);
    }

    public static MultiPoligonoGeo DePoligono(IReadOnlyList<IReadOnlyList<double[]>> aneis) => new([aneis]);

    public bool Contem(PontoGeo p)
    {
        var (minLon, minLat, maxLon, maxLat) = Envelope;
        if (p.Longitude < minLon || p.Longitude > maxLon || p.Latitude < minLat || p.Latitude > maxLat) return false;
        foreach (var poligono in Poligonos)
        {
            if (!DentroDoAnel(poligono[0], p)) continue;
            var emBuraco = false;
            for (var i = 1; i < poligono.Count; i++)
            {
                if (DentroDoAnel(poligono[i], p)) { emBuraco = true; break; }
            }
            if (!emBuraco) return true;
        }
        return false;
    }

    /// <summary>Ray casting (par/ímpar). Pontos exatamente na borda podem cair para qualquer lado.</summary>
    private static bool DentroDoAnel(IReadOnlyList<double[]> anel, PontoGeo p)
    {
        var dentro = false;
        double x = p.Longitude, y = p.Latitude;
        for (int i = 0, j = anel.Count - 1; i < anel.Count; j = i++)
        {
            double xi = anel[i][0], yi = anel[i][1], xj = anel[j][0], yj = anel[j][1];
            var cruza = (yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / ((yj - yi) == 0 ? double.Epsilon : (yj - yi)) + xi;
            if (cruza) dentro = !dentro;
        }
        return dentro;
    }

    /// <summary>Distância aproximada (m) do ponto ao vértice/aresta mais próxima — usada só para tolerância de borda.</summary>
    public double DistanciaAproximadaBordaMetros(PontoGeo p)
    {
        var menor = double.MaxValue;
        foreach (var anel in Poligonos.SelectMany(x => x))
        {
            for (var i = 0; i < anel.Count - 1; i++)
            {
                var d = DistanciaSegmento(p, anel[i], anel[i + 1]);
                if (d < menor) menor = d;
            }
        }
        return menor;
    }

    private static double DistanciaSegmento(PontoGeo p, double[] a, double[] b)
    {
        // projeção equiretangular local — suficiente para distâncias curtas
        var cosLat = Math.Cos(p.Latitude * Math.PI / 180);
        double Px = p.Longitude * cosLat, Py = p.Latitude, Ax = a[0] * cosLat, Ay = a[1], Bx = b[0] * cosLat, By = b[1];
        double dx = Bx - Ax, dy = By - Ay;
        var t = dx == 0 && dy == 0 ? 0 : Math.Clamp(((Px - Ax) * dx + (Py - Ay) * dy) / (dx * dx + dy * dy), 0, 1);
        double cx = Ax + t * dx, cy = Ay + t * dy;
        var graus = Math.Sqrt((Px - cx) * (Px - cx) + (Py - cy) * (Py - cy));
        return graus * 111_320d;
    }
}
