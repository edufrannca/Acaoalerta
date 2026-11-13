using System.Collections.Concurrent;
using System.Xml.Linq;
using AlertaEmAcao.Modulos.Comum;
using AlertaEmAcao.Modulos.Comunicacao;
using AlertaEmAcao.Modulos.InteligenciaTerritorial;
using AlertaEmAcao.Modulos.Municipios;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AlertaEmAcao.Infraestrutura.Persistencia;

public sealed class UnidadeTrabalhoEf(AlertaDbContext db) : IUnidadeTrabalho
{
    public IQueryable<T> Consulta<T>() where T : class => db.Set<T>();
    public void Adicionar<T>(T entidade) where T : class => db.Set<T>().Add(entidade);
    public void Remover<T>(T entidade) where T : class => db.Set<T>().Remove(entidade);
    public Task<List<T>> ListarAsync<T>(IQueryable<T> consulta, CancellationToken ct = default) => consulta.ToListAsync(ct);
    public Task<T?> PrimeiroAsync<T>(IQueryable<T> consulta, CancellationToken ct = default) => consulta.FirstOrDefaultAsync(ct);
    public Task<int> ContarAsync<T>(IQueryable<T> consulta, CancellationToken ct = default) => consulta.CountAsync(ct);
    public Task<int> SalvarAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

/// <summary>Repositório de chaves do Data Protection no PostgreSQL.</summary>
public sealed class RepositorioChavesProtecao(IServiceScopeFactory escopos) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var s = escopos.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AlertaDbContext>();
        return db.ChavesProtecao.AsNoTracking().Where(c => c.Xml != null).Select(c => c.Xml!).ToList().Select(XElement.Parse).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var s = escopos.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AlertaDbContext>();
        db.ChavesProtecao.Add(new ChaveProtecao { FriendlyName = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });
        db.SaveChanges();
    }
}

public sealed class CatalogoMunicipios(IServiceScopeFactory escopos, ILogger<CatalogoMunicipios> log) : ICatalogoMunicipios
{
    private volatile IReadOnlyList<Municipio> _municipios = [];
    private volatile IdentificadorMunicipio _identificador = new([]);

    public IReadOnlyList<Municipio> Listar() => _municipios;

    public MunicipioIdentificado? Identificar(PontoGeo ponto) => _identificador.Identificar(ponto);

    public async Task RecarregarAsync(CancellationToken ct)
    {
        using var s = escopos.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AlertaDbContext>();
        var lista = await db.Set<Municipio>().AsNoTracking().OrderBy(m => m.Nome).ToListAsync(ct);
        var limites = new List<(string, string, MultiPoligonoGeo)>();
        foreach (var m in lista)
        {
            try { limites.Add((m.CodigoIbge, m.Nome, LeitorGeoJson.LerGeometria(m.GeometriaGeoJson))); }
            catch (Exception e) { log.LogError(e, "Geometria inválida para {Municipio}", m.Nome); }
        }
        _municipios = lista;
        _identificador = new IdentificadorMunicipio(limites);
        log.LogInformation("Catálogo de municípios carregado: {N} municípios", lista.Count);
    }
}

public sealed class ConsultaEspacialPostgis(AlertaDbContext db) : IConsultaEspacial
{
    public async Task<IReadOnlyList<(Guid LocalId, Guid UsuarioId, string Apelido)>> LocaisAbrangidosPorAvisoAsync(Guid avisoId, CancellationToken ct)
    {
        var linhas = await db.Database.SqlQuery<LinhaLocal>($"""
            SELECT l."Id" AS "LocalId", l."UsuarioId", l."Apelido"
            FROM alerta.locais_monitorados l
            JOIN alerta.usuarios u ON u."Id" = l."UsuarioId"
            JOIN alerta.avisos a ON a."Id" = {avisoId}
            WHERE l."Ativo" AND u."ConsenteAlertas"
              AND ((a.geom IS NOT NULL AND ST_Intersects(a.geom, l.geom))
                OR (a.geom IS NULL AND l."MunicipioCodigo" = ANY(a."MunicipiosCodigos")))
            """).ToListAsync(ct);
        return linhas.Select(x => (x.LocalId, x.UsuarioId, x.Apelido)).ToList();
    }

    public async Task<string?> MunicipioPorPontoAsync(PontoGeo p, CancellationToken ct)
    {
        var r = await db.Database.SqlQuery<string>($"""
            SELECT "CodigoIbge" AS "Value" FROM alerta.municipios
            WHERE ST_Contains(geom, ST_SetSRID(ST_MakePoint({p.Longitude}, {p.Latitude}), 4326)) LIMIT 1
            """).ToListAsync(ct);
        return r.FirstOrDefault();
    }

    public sealed class LinhaLocal { public Guid LocalId { get; set; } public Guid UsuarioId { get; set; } public string Apelido { get; set; } = ""; }
}

public sealed class RegistroFontes : IRegistroFontes
{
    private readonly ConcurrentDictionary<string, EstadoFonte> _estados = new();

    public void Registrar(string nome, bool sucesso, string? erro, int? registros, string? url, DateTimeOffset em) =>
        _estados.AddOrUpdate(nome,
            _ => new EstadoFonte(nome, sucesso, em, sucesso ? em : null, sucesso ? null : erro, registros, url),
            (_, a) => a with
            {
                Disponivel = sucesso, UltimaConsultaEm = em, UltimoSucessoEm = sucesso ? em : a.UltimoSucessoEm,
                UltimoErro = sucesso ? null : erro, Registros = sucesso ? registros : a.Registros, Url = url ?? a.Url,
            });

    public IReadOnlyList<EstadoFonte> Listar() => _estados.Values.OrderBy(e => e.Nome).ToList();
    public EstadoFonte? Obter(string nome) => _estados.GetValueOrDefault(nome);
}
