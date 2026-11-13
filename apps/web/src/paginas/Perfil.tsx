import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { Botao, Campo, Cartao, Selo, Vazio, classeEntrada } from '../componentes/Base'
import { api, formatarHora } from '../lib/api'
import { useDados } from '../lib/hooks'
import { MUNICIPIOS, nomeMunicipio } from '../lib/municipios'
import { useSessao } from '../lib/sessao'
import { CONSENTIMENTOS, NOTA_SOCORRO, ROTULO_TIPO, type LocalMonitorado } from '../lib/residencias'
import { useEvento } from '../lib/tempoReal'


export function Perfil() {
  const { usuario, carregando } = useSessao()
  if (carregando) return <p>Carregando…</p>
  return usuario ? <PerfilLogado /> : <Entrar />
}

function Entrar() {
  const { entrar, registrar } = useSessao()
  const [modo, setModo] = useState<'entrar' | 'registrar'>('entrar')
  const [d, setD] = useState({ nome: '', email: '', senha: '', municipioCodigo: '', telefone: '' })
  // Todos opcionais e desmarcados: criar conta não exige nenhuma autorização.
  const [cons, setCons] = useState<Record<string, boolean>>({ alertasPreventivos: false, compartilhamentoOperacional: false, compartilhamentoComunitario: false, notificacaoFamiliares: false })
  const [erro, setErro] = useState<string | null>(null)
  const executar = async (f: () => Promise<void>) => { setErro(null); try { await f() } catch (e) { setErro(e instanceof Error ? e.message : 'Falha') } }

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Cartao titulo={modo === 'entrar' ? 'Entrar' : 'Criar conta'}>
        <p className="mb-3 text-[14px] text-tinta-suave">A conta é opcional: qualquer pessoa pode pedir socorro sem cadastro. Com ela você cadastra a família e recebe alertas para locais monitorados.</p>
        <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); void executar(() => modo === 'entrar' ? entrar(d.email, d.senha) : registrar({ ...d, municipioCodigo: d.municipioCodigo || undefined, telefone: d.telefone || undefined, consentimentos: cons })) }}>
          {modo === 'registrar' && <Campo rotulo="Nome"><input required className={classeEntrada} value={d.nome} onChange={(e) => setD({ ...d, nome: e.target.value })} autoComplete="name" /></Campo>}
          <Campo rotulo="E-mail"><input required type="email" className={classeEntrada} value={d.email} onChange={(e) => setD({ ...d, email: e.target.value })} autoComplete="email" /></Campo>
          <Campo rotulo="Senha" dica={modo === 'registrar' ? 'Mínimo de 8 caracteres.' : undefined}><input required type="password" minLength={8} className={classeEntrada} value={d.senha} onChange={(e) => setD({ ...d, senha: e.target.value })} autoComplete={modo === 'entrar' ? 'current-password' : 'new-password'} /></Campo>
          {modo === 'registrar' && (
            <>
              <Campo rotulo="Telefone (opcional)"><input className={classeEntrada} value={d.telefone} onChange={(e) => setD({ ...d, telefone: e.target.value })} autoComplete="tel" /></Campo>
              <Campo rotulo="Município"><select className={classeEntrada} value={d.municipioCodigo} onChange={(e) => setD({ ...d, municipioCodigo: e.target.value })}><option value="">Prefiro não informar</option>{Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}</select></Campo>
              <fieldset className="grid gap-2 rounded-controle bg-fundo p-3">
                <legend className="px-1 text-[14px] font-semibold">Autorizações (opcionais)</legend>
                {CONSENTIMENTOS.map((c) => (
                  <label key={c.chave} className="flex items-start gap-2 text-[13.5px]">
                    <input type="checkbox" className="mt-0.5 size-5 shrink-0 accent-oceano" checked={cons[c.chave]} onChange={(e) => setCons({ ...cons, [c.chave]: e.target.checked })} />
                    <span><strong>{c.titulo}.</strong> {c.texto}</span>
                  </label>
                ))}
                <p className="text-[12.5px] text-tinta-suave">{NOTA_SOCORRO} A permissão de GPS do celular é separada.</p>
              </fieldset>
              <p className="text-[12.5px] text-tinta-suave">Não pedimos CPF. Seus dados de família são cifrados e usados apenas nos seus pedidos de socorro.</p>
            </>
          )}
          {erro && <p role="alert" className="text-[14px] text-emergencia-escura">{erro}</p>}
          <Botao type="submit">{modo === 'entrar' ? 'Entrar' : 'Criar conta'}</Botao>
          <button type="button" className="text-[14px] text-oceano underline-offset-2 hover:underline" onClick={() => setModo(modo === 'entrar' ? 'registrar' : 'entrar')}>
            {modo === 'entrar' ? 'Criar uma conta' : 'Já tenho conta'}
          </button>
        </form>
      </Cartao>
      <div className="grid content-start gap-4">
        <Cartao titulo="Recuperar acesso">
          <p className="text-[14.5px]">Esqueceu a senha? A recuperação por e-mail ainda não está disponível nesta versão de demonstração. Você pode pedir socorro sem conta a qualquer momento.</p>
        </Cartao>
        <Cartao titulo="Neste aparelho">
          <div className="grid gap-2">
            <Link to="/pedidos" className="flex min-h-11 items-center rounded-controle bg-fundo px-3 text-[15px] font-semibold text-petroleo">Meus pedidos e envios</Link>
            <Link to="/instalar" className="flex min-h-11 items-center rounded-controle bg-fundo px-3 text-[15px] font-semibold text-petroleo">Instalar o app no celular</Link>
          </div>
        </Cartao>
        <p className="text-[13px] text-tinta-suave"><Link to="/demo" className="font-semibold text-oceano underline">Acesso das equipes de atendimento</Link></p>
      </div>
    </div>
  )
}

interface Integrante { apelido?: string | null; idade: number | null; mobilidadeReduzida: boolean; dependeAssistencia: boolean; usaEquipamentoMedico: boolean; necessidadesEspeciais?: string | null }
interface Notif { id: string; titulo: string; mensagem: string; criadaEm: string; resposta: string | null; avisoOficial: boolean; entregas: { canal: string; resultado: string; em: string }[] }

function PerfilLogado() {
  const { usuario, sair, recarregar } = useSessao()
  const u = usuario!
  const [perfil, setPerfil] = useState({ telefone: '', municipioCodigo: u.municipioCodigo ?? '', enderecoReferencia: u.enderecoReferencia ?? '', consenteAlertas: u.consenteAlertas, consenteLocalizacao: u.consenteLocalizacao, receberPush: u.receberPush })
  const [msg, setMsg] = useState<string | null>(null)
  const operador = ['OperadorCentral', 'Administrador', 'Organizacao'].includes(u.papel)

  const salvar = async () => {
    // Autorizações são alteradas no cartão próprio (com histórico); aqui seguem os valores atuais.
    try { await api('/api/eu/perfil', { method: 'PUT', json: { nome: u.nome, ...perfil, consenteAlertas: u.consenteAlertas, consenteLocalizacao: u.consenteLocalizacao, municipioCodigo: perfil.municipioCodigo || null } }); await recarregar(); setMsg('Preferências salvas.') }
    catch (e) { setMsg(e instanceof Error ? e.message : 'Falha') }
  }

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <div className="grid content-start gap-4">
        <Cartao titulo={u.nome} acao={<Botao variante="fantasma" onClick={sair}>Sair</Botao>}>
          <p className="text-[14px]">{u.email}</p>
          <Link to="/pedidos" className="mt-2 inline-flex min-h-11 items-center text-[15px] font-semibold text-oceano underline">Meus pedidos e envios</Link>
          <div className="mt-2 flex flex-wrap gap-2"><Selo>{u.papel}</Selo>{u.contaDemonstracao && <Selo tom="demo">Conta fictícia</Selo>}{u.centralCodigo && <Selo tom="espera">{u.centralCodigo}</Selo>}</div>
          {operador && (
            <div className="mt-3 flex flex-wrap gap-2">
              <Link to="/painel" className="rounded-controle bg-petroleo px-3 py-2 text-[14px] font-semibold text-branco">Painel de coordenação</Link>
              {u.centralCodigo && <Link to={`/central/${u.centralCodigo}`} className="rounded-controle bg-branco px-3 py-2 text-[14px] font-semibold text-petroleo ring-1 ring-linha">Central receptora simulada</Link>}
              {u.papel === 'Administrador' && <Link to="/demo" className="rounded-controle bg-[#3E2F5E] px-3 py-2 text-[14px] font-semibold text-branco">Controle da demonstração</Link>}
            </div>
          )}
        </Cartao>
        <Consentimentos />
        <Cartao titulo="Preferências">
          <div className="grid gap-3">
            <Campo rotulo="Município"><select className={classeEntrada} value={perfil.municipioCodigo} onChange={(e) => setPerfil({ ...perfil, municipioCodigo: e.target.value })}><option value="">Não informado</option>{Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}</select></Campo>
            <Campo rotulo="Endereço de referência" dica="Sua residência. Em um pedido de socorro usamos a localização atual, não este endereço."><input className={classeEntrada} value={perfil.enderecoReferencia} onChange={(e) => setPerfil({ ...perfil, enderecoReferencia: e.target.value })} /></Campo>
            <label className="flex items-start gap-2 text-[14.5px]"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={perfil.receberPush} onChange={(e) => setPerfil({ ...perfil, receberPush: e.target.checked })} />Receber notificações push neste aparelho (exige a autorização de alertas preventivos)</label>
            <Botao onClick={salvar}>Salvar preferências</Botao>
            <AtivarPush habilitado={perfil.receberPush && u.consenteAlertas} />
            {msg && <p role="status" className="text-[14px] text-seguro">{msg}</p>}
          </div>
        </Cartao>
        <NotificacoesFamilia />
        <NotificacoesPreventivas />
        <Notificacoes />
      </div>
      <div className="grid content-start gap-4">
        <Acompanhar />
        <LocaisMonitorados />
        <Familia />
      </div>
    </div>
  )
}

function AtivarPush({ habilitado }: { habilitado: boolean }) {
  const [estado, setEstado] = useState<string | null>(null)
  const suporte = typeof window !== 'undefined' && 'serviceWorker' in navigator && 'PushManager' in window
  const ativar = async () => {
    try {
      const { chave, motivo } = await api<{ chave: string | null; motivo?: string }>('/api/push/chave-publica')
      if (!chave) { setEstado(motivo ?? 'Push indisponível neste ambiente. Os avisos continuam visíveis no app.'); return }
      const perm = await Notification.requestPermission()
      if (perm !== 'granted') { setEstado('Permissão de notificações negada.'); return }
      const reg = await navigator.serviceWorker.ready
      const pad = '='.repeat((4 - (chave.length % 4)) % 4)
      const bin = atob((chave + pad).replace(/-/g, '+').replace(/_/g, '/'))
      const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: Uint8Array.from(bin, (c) => c.charCodeAt(0)) })
      const j = sub.toJSON()
      await api('/api/eu/push/assinatura', { method: 'POST', json: { endpoint: j.endpoint, p256dh: j.keys?.p256dh, auth: j.keys?.auth } })
      setEstado('Notificações ativadas neste aparelho. A entrega depende do serviço de push do navegador e da conexão.')
    } catch (e) { setEstado(e instanceof Error ? e.message : 'Falha ao ativar') }
  }
  if (!suporte) return <p className="text-[13px] text-tinta-suave">Este navegador não suporta notificações push. No iPhone, instale o app na tela de início (iOS 16.4+) para habilitá-las. Os avisos continuam aparecendo no app.</p>
  return (
    <div>
      <Botao variante="secundario" disabled={!habilitado} onClick={ativar}>Ativar notificações neste aparelho</Botao>
      {estado && <p className="mt-1 text-[13px]">{estado}</p>}
    </div>
  )
}

function Familia() {
  const { dados, recarregar } = useDados<{ integrantes: Integrante[]; atualizadoEm: string | null }>('/api/eu/familia')
  const [lista, setLista] = useState<Integrante[]>([])
  const [msg, setMsg] = useState<string | null>(null)
  useEffect(() => { if (dados) setLista(dados.integrantes) }, [dados])
  const novo = (): Integrante => ({ apelido: '', idade: null, mobilidadeReduzida: false, dependeAssistencia: false, usaEquipamentoMedico: false, necessidadesEspeciais: '' })
  const salvar = async () => {
    try { await api('/api/eu/familia', { method: 'PUT', json: { integrantes: lista, contatos: [] } }); setMsg('Família salva (dados cifrados).'); void recarregar() }
    catch (e) { setMsg(e instanceof Error ? e.message : 'Falha') }
  }
  return (
    <Cartao titulo="Minha família" acao={<span className="text-[13px] text-tinta-suave">{lista.length} pessoa(s)</span>}>
      <p className="mb-3 text-[13.5px] text-tinta-suave">Usado como referência nos seus pedidos de socorro. O que você informar no momento do pedido prevalece.</p>
      <ul className="grid gap-3">
        {lista.map((p, i) => {
          const set = (m: Partial<Integrante>) => setLista(lista.map((x, j) => (j === i ? { ...x, ...m } : x)))
          return (
            <li key={i} className="rounded-controle bg-areia p-3">
              <div className="grid grid-cols-[1fr_90px_auto] items-end gap-2">
                <Campo rotulo="Nome ou apelido"><input className={classeEntrada} value={p.apelido ?? ''} onChange={(e) => set({ apelido: e.target.value })} /></Campo>
                <Campo rotulo="Idade"><input type="number" min={0} max={125} className={classeEntrada} value={p.idade ?? ''} onChange={(e) => set({ idade: e.target.value === '' ? null : Number(e.target.value) })} /></Campo>
                <Botao variante="fantasma" aria-label={`Remover ${p.apelido || 'pessoa'}`} onClick={() => setLista(lista.filter((_, j) => j !== i))}>Remover</Botao>
              </div>
              <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-[14px]">
                <label className="flex items-center gap-1.5"><input type="checkbox" className="size-4 accent-oceano" checked={p.mobilidadeReduzida} onChange={(e) => set({ mobilidadeReduzida: e.target.checked })} />Mobilidade reduzida</label>
                <label className="flex items-center gap-1.5"><input type="checkbox" className="size-4 accent-oceano" checked={p.dependeAssistencia} onChange={(e) => set({ dependeAssistencia: e.target.checked })} />Depende de assistência</label>
                <label className="flex items-center gap-1.5"><input type="checkbox" className="size-4 accent-oceano" checked={p.usaEquipamentoMedico} onChange={(e) => set({ usaEquipamentoMedico: e.target.checked })} />Usa equipamento médico</label>
              </div>
            </li>
          )
        })}
      </ul>
      <div className="mt-3 flex gap-2"><Botao variante="secundario" onClick={() => setLista([...lista, novo()])}>Adicionar pessoa</Botao><Botao onClick={salvar}>Salvar família</Botao></div>
      {msg && <p role="status" className="mt-2 text-[14px] text-seguro">{msg}</p>}
    </Cartao>
  )
}

function LocaisMonitorados() {
  const { dados } = useDados<LocalMonitorado[]>('/api/eu/locais')
  const cheio = (dados?.length ?? 0) >= 5
  return (
    <Cartao titulo="Locais monitorados" acao={<span className="text-[13px] text-tinta-suave">{dados?.length ?? 0} de 5</span>}>
      <p className="mb-3 text-[13.5px] text-tinta-suave">Sua casa, o trabalho ou a casa de um familiar.</p>
      {(dados ?? []).length === 0 ? <Vazio>Nenhum local monitorado.</Vazio> : (
        <ul className="grid gap-2">
          {dados!.map((l) => (
            <li key={l.id}>
              <Link to={`/locais/${l.id}`} className="flex min-h-12 items-center justify-between rounded-controle bg-areia px-3 py-2">
                <span><span className="font-semibold">{l.apelido}</span><span className="block text-[13px] text-tinta-suave">{ROTULO_TIPO[l.tipo]}{l.moroAqui ? ' · onde moro' : ''} · {l.municipio ?? nomeMunicipio(l.municipioCodigo)}</span></span>
                <span aria-hidden="true" className="text-oceano">›</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
      {cheio ? <p className="mt-3 text-[13.5px]">Limite de 5 locais atingido. Remova ou edite um local para trocar.</p>
        : <Link to="/locais/novo" className="mt-3 flex min-h-11 items-center justify-center rounded-controle bg-petroleo px-4 text-[15px] font-semibold text-branco">Adicionar local</Link>}
    </Cartao>
  )
}

/** Morador: gera o código para um familiar acompanhar a casa em tempo real. */
function ConvidarFamiliar() {
  const { dados } = useDados<LocalMonitorado[]>('/api/eu/locais')
  const casa = (dados ?? []).find((l) => l.tipo === 'Residencia' && l.moroAqui)
  const [convite, setConvite] = useState<{ codigo: string; expiraEm: string } | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const gerar = async () => {
    setErro(null)
    try { setConvite(await api(`/api/eu/locais/${casa!.id}/convites`, { method: 'POST' })) } catch (e) { setErro(e instanceof Error ? e.message : 'Não foi possível gerar o código.') }
  }
  return (
    <div>
      <h3 className="text-[16px] font-bold text-petroleo">Convidar um familiar para acompanhar minha casa</h3>
      {!dados ? null : !casa ? (
        <>
          <p className="mt-1 text-[14px]">Primeiro cadastre sua casa e marque “Eu moro aqui”.</p>
          <Link to="/locais/novo" className="mt-2 flex min-h-11 items-center justify-center rounded-controle bg-petroleo px-4 text-[15px] font-semibold text-branco">Cadastrar minha casa</Link>
        </>
      ) : (
        <>
          <Botao className="mt-2 w-full" onClick={gerar}>Gerar código de convite</Botao>
          {convite && (
            <div role="status" className="mt-2 rounded-controle bg-oceano-suave p-3">
              <p className="numero-tabular select-all text-[26px] font-bold tracking-widest text-petroleo">{convite.codigo}</p>
              <p className="text-[13.5px]">Envie ao familiar. Vale uma vez, até {formatarHora(convite.expiraEm)}.</p>
            </div>
          )}
          <Link to={`/locais/${casa.id}`} className="mt-2 inline-flex min-h-11 items-center text-[15px] font-semibold text-oceano underline">Aprovar familiares de “{casa.apelido}”</Link>
          {erro && <p role="alert" className="text-[14px] text-emergencia-escura">{erro}</p>}
        </>
      )}
    </div>
  )
}

interface Acompanhado { id: string; apelidoParaFamiliar: string; status: string; municipio: string | null }

/** Familiar: usa o código recebido do morador. Nada é liberado antes da aprovação dele. */
function Acompanhar() {
  const { dados, recarregar } = useDados<Acompanhado[]>('/api/eu/acompanho', { intervaloMs: 60000 })
  const [codigo, setCodigo] = useState('')
  const [apelido, setApelido] = useState('')
  const [msg, setMsg] = useState<{ t: string; erro?: boolean } | null>(null)
  const usar = async () => {
    try { const r = await api<{ mensagem: string }>('/api/eu/acompanhar', { method: 'POST', json: { codigo, apelido } }); setMsg({ t: r.mensagem }); setCodigo(''); setApelido(''); void recarregar() }
    catch (e) { setMsg({ t: e instanceof Error ? e.message : 'Falha', erro: true }) }
  }
  const sairDe = async (id: string) => { await api(`/api/eu/vinculos/${id}`, { method: 'DELETE' }); void recarregar() }
  return (
    <Cartao titulo="Família">
      <ConvidarFamiliar />
      <h3 className="mt-5 text-[16px] font-bold text-petroleo">Acompanhar a casa de um familiar</h3>
      <p className="mb-2 text-[13.5px] text-tinta-suave">Digite o código que ele gerou. Você será avisado se alguém pedir socorro lá.</p>
      <div className="grid gap-2 sm:grid-cols-2">
        <Campo rotulo="Código de convite"><input className={`${classeEntrada} uppercase tracking-widest`} value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="ABCDE-FGHJK" autoComplete="off" /></Campo>
        <Campo rotulo="Como você chama essa casa"><input className={classeEntrada} maxLength={60} value={apelido} onChange={(e) => setApelido(e.target.value)} placeholder="Casa da minha mãe" /></Campo>
      </div>
      <Botao className="mt-2" onClick={usar} disabled={codigo.replace(/[^a-z0-9]/gi, '').length < 10 || !apelido.trim()}>Enviar pedido de acompanhamento</Botao>
      {msg && <p role={msg.erro ? 'alert' : 'status'} className={`mt-2 text-[14px] ${msg.erro ? 'text-emergencia-escura' : 'text-seguro'}`}>{msg.t}</p>}
      {(dados ?? []).length > 0 && (
        <ul className="mt-3 grid gap-2">
          {dados!.map((v) => (
            <li key={v.id} className="flex flex-wrap items-center justify-between gap-2 rounded-controle bg-areia px-3 py-2">
              <span><span className="font-semibold">{v.apelidoParaFamiliar}</span><span className="block text-[13px] text-tinta-suave">{v.status === 'Ativo' ? `Ativo · ${v.municipio ?? ''}` : v.status === 'AguardandoAprovacao' ? 'Aguardando aprovação do morador' : 'Encerrado'}</span></span>
              {(v.status === 'Ativo' || v.status === 'AguardandoAprovacao') && <Botao variante="fantasma" onClick={() => sairDe(v.id)}>Deixar de acompanhar</Botao>}
            </li>
          ))}
        </ul>
      )}
    </Cartao>
  )
}

interface NotifFamilia { id: string; titulo: string; mensagem: string; ocorrenciaId: string; criadaEm: string; lidaEm: string | null; entregas: { canal: string; resultado: string }[] }

interface NotifPreventiva { id: string; localId: string; nivel: string; titulo: string; mensagem: string; url: string | null; criadaEm: string; lidaEm: string | null }

function NotificacoesPreventivas() {
  const { dados, recarregar } = useDados<NotifPreventiva[]>('/api/eu/notificacoes-preventivas', { intervaloMs: 60000 })
  useEvento('notificacao', () => { void recarregar() })
  if (!dados || dados.length === 0) return null
  return (
    <Cartao titulo="Avisos preventivos de chuva">
      <p className="mb-2 text-[13px] text-tinta-suave">Avisos para locais monitorados quando a atenção a alagamentos aumenta. Não confirmam alagamento.</p>
      <ul className="grid gap-2">
        {dados.slice(0, 10).map((n) => (
          <li key={n.id}>
            <Link to={n.url ?? '/'} onClick={() => { void api(`/api/eu/notificacoes-preventivas/${n.id}/lida`, { method: 'POST' }) }}
              className={`block rounded-controle p-3 text-[14px] ${n.lidaEm ? 'bg-areia' : 'bg-atencao-claro ring-1 ring-atencao'}`}>
              <span className="font-semibold">{n.titulo}</span>{!n.lidaEm && <span className="ml-2 text-[12.5px] font-semibold">(novo)</span>}
              <span className="block">{n.mensagem}</span>
              <span className="mt-1 block text-[12.5px] text-tinta-suave">{formatarHora(n.criadaEm)}</span>
            </Link>
          </li>
        ))}
      </ul>
    </Cartao>
  )
}

function NotificacoesFamilia() {
  const { dados, recarregar } = useDados<NotifFamilia[]>('/api/eu/notificacoes-familia', { intervaloMs: 30000 })
  useEvento('notificacao', () => { void recarregar() })
  if (!dados || dados.length === 0) return null
  return (
    <Cartao titulo="Avisos de familiares">
      <ul className="grid gap-2">
        {dados.slice(0, 10).map((n) => (
          <li key={n.id}>
            <Link to={`/familia/pedidos/${n.ocorrenciaId}`} onClick={() => { void api(`/api/eu/notificacoes-familia/${n.id}/lida`, { method: 'POST' }) }}
              className={`block rounded-controle p-3 text-[14px] ${n.lidaEm ? 'bg-areia' : 'bg-emergencia-claro ring-1 ring-emergencia'}`}>
              <span className="font-semibold">{n.titulo}</span>
              <span className="block">{n.mensagem}</span>
              <span className="mt-1 block text-[12.5px] text-tinta-suave">{formatarHora(n.criadaEm)} · guardado no servidor{n.entregas.some((e) => e.canal === 'web_push' && e.resultado === 'aceito_pelo_servico_push') ? ' · push enviado' : ''}</span>
            </Link>
          </li>
        ))}
      </ul>
    </Cartao>
  )
}

interface EstadoConsentimentos { versao: string; finalidades: { finalidade: string; texto: string; concedido: boolean }[]; historico: { finalidade: string; concedido: boolean; versaoTexto: string; origem: string; em: string }[]; nota: string }

function Consentimentos() {
  const { recarregar: recarregarSessao } = useSessao()
  const { dados, recarregar } = useDados<EstadoConsentimentos>('/api/eu/consentimentos')
  const [msg, setMsg] = useState<string | null>(null)
  const mudar = async (finalidade: string, concedido: boolean) => {
    try {
      const r = await api<{ pedidosRetiradosDaComunidade: number }>('/api/eu/consentimentos', { method: 'PUT', json: { finalidade, concedido } })
      setMsg(concedido ? 'Autorização registrada.' : `Autorização revogada.${r.pedidosRetiradosDaComunidade ? ` ${r.pedidosRetiradosDaComunidade} pedido(s) saíram da lista da comunidade.` : ''}`)
      void recarregar(); void recarregarSessao()
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Falha') }
  }
  const titulo = (f: string) => CONSENTIMENTOS.find((c) => c.finalidade === f)?.titulo ?? f
  return (
    <Cartao titulo="Autorizações (LGPD)">
      <div className="grid gap-3">
        {(dados?.finalidades ?? []).map((f) => (
          <label key={f.finalidade} className="flex items-start gap-2 text-[14px]">
            <input type="checkbox" className="mt-0.5 size-5 shrink-0 accent-oceano" checked={f.concedido} onChange={(e) => void mudar(f.finalidade, e.target.checked)} />
            <span><strong>{titulo(f.finalidade)}.</strong> {f.texto}</span>
          </label>
        ))}
      </div>
      <p className="mt-3 text-[12.5px] text-tinta-suave">{dados?.nota}</p>
      {msg && <p role="status" className="mt-2 text-[14px] text-seguro">{msg}</p>}
      {dados && dados.historico.length > 0 && (
        <details className="mt-2 text-[13px]">
          <summary className="min-h-11 cursor-pointer font-semibold">Histórico ({dados.historico.length})</summary>
          <ul className="grid gap-1">{dados.historico.map((h, i) => <li key={i}>{formatarHora(h.em)} · {titulo(h.finalidade)}: {h.concedido ? 'concedida' : 'revogada'} ({h.origem}, texto {h.versaoTexto})</li>)}</ul>
        </details>
      )}
    </Cartao>
  )
}

function Notificacoes() {
  const { dados } = useDados<Notif[]>('/api/eu/notificacoes', { intervaloMs: 30000 })
  return (
    <Cartao titulo="Notificações recebidas">
      {(dados ?? []).length === 0 ? <Vazio>Nenhuma notificação ainda.</Vazio> : (
        <ul className="grid gap-2">
          {dados!.slice(0, 8).map((n) => (
            <li key={n.id} className="rounded-controle bg-areia p-3 text-[14px]">
              <div className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold">{n.titulo}</span><Selo tom={n.avisoOficial ? 'erro' : 'demo'}>{n.avisoOficial ? 'Oficial' : 'Demonstrativo'}</Selo></div>
              <p>{n.mensagem}</p>
              <p className="mt-1 text-[12.5px] text-tinta-suave">{formatarHora(n.criadaEm)}. Resposta: {n.resposta ?? 'pendente'}. Canais: {n.entregas.map((e) => `${e.canal} (${e.resultado})`).join(', ')}</p>
            </li>
          ))}
        </ul>
      )}
    </Cartao>
  )
}
