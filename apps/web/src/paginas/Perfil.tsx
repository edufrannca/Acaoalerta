import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { Botao, Campo, Cartao, Selo, Vazio, classeEntrada } from '../componentes/Base'
import { api, formatarHora } from '../lib/api'
import { useDados, useLocalizacao } from '../lib/hooks'
import { MUNICIPIOS, nomeMunicipio } from '../lib/municipios'
import { useSessao } from '../lib/sessao'

const PERFIS_DEMO: { perfil: string; rotulo: string; descricao: string }[] = [
  { perfil: 'cidadao', rotulo: 'Ana, cidadã (Recife)', descricao: 'Família cadastrada e 2 locais monitorados' },
  { perfil: 'cidadao-olinda', rotulo: 'João, cidadão (Olinda)', descricao: 'Local monitorado em Olinda' },
  { perfil: 'voluntario', rotulo: 'Bia, voluntária', descricao: 'Ofertas de apoio' },
  { perfil: 'defesa-civil-recife', rotulo: 'Defesa Civil Recife (simulada)', descricao: 'Painel e central receptora' },
  { perfil: 'defesa-civil-olinda', rotulo: 'Defesa Civil Olinda (simulada)', descricao: 'Painel e central receptora' },
  { perfil: 'defesa-civil-ipojuca', rotulo: 'Defesa Civil Ipojuca (simulada)', descricao: 'Painel e central receptora' },
  { perfil: 'bombeiros', rotulo: 'Bombeiros (receptor simulado)', descricao: 'Ocorrências hidrológicas, geológicas e feridos' },
  { perfil: 'samu', rotulo: 'SAMU (receptor simulado)', descricao: 'Feridos e necessidades médicas' },
  { perfil: 'organizacao', rotulo: 'Organização de apoio (fictícia)', descricao: 'Recebe ofertas de ajuda' },
  { perfil: 'admin', rotulo: 'Administrador da demonstração', descricao: 'Cenários, avisos e falhas de centrais' },
]

export function Perfil() {
  const { usuario, carregando } = useSessao()
  if (carregando) return <p>Carregando…</p>
  return usuario ? <PerfilLogado /> : <Entrar />
}

function Entrar() {
  const { entrar, entrarDemo, registrar } = useSessao()
  const [modo, setModo] = useState<'entrar' | 'registrar'>('entrar')
  const [d, setD] = useState({ nome: '', email: '', senha: '', municipioCodigo: '', telefone: '' })
  const [erro, setErro] = useState<string | null>(null)
  const executar = async (f: () => Promise<void>) => { setErro(null); try { await f() } catch (e) { setErro(e instanceof Error ? e.message : 'Falha') } }

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Cartao titulo={modo === 'entrar' ? 'Entrar' : 'Criar conta'}>
        <p className="mb-3 text-[14px] text-tinta-suave">A conta é opcional: qualquer pessoa pode pedir socorro sem cadastro. Com ela você cadastra a família e recebe alertas para locais monitorados.</p>
        <form className="grid gap-3" onSubmit={(e) => { e.preventDefault(); void executar(() => modo === 'entrar' ? entrar(d.email, d.senha) : registrar({ ...d, municipioCodigo: d.municipioCodigo || undefined, telefone: d.telefone || undefined })) }}>
          {modo === 'registrar' && <Campo rotulo="Nome"><input required className={classeEntrada} value={d.nome} onChange={(e) => setD({ ...d, nome: e.target.value })} autoComplete="name" /></Campo>}
          <Campo rotulo="E-mail"><input required type="email" className={classeEntrada} value={d.email} onChange={(e) => setD({ ...d, email: e.target.value })} autoComplete="email" /></Campo>
          <Campo rotulo="Senha" dica={modo === 'registrar' ? 'Mínimo de 8 caracteres.' : undefined}><input required type="password" minLength={8} className={classeEntrada} value={d.senha} onChange={(e) => setD({ ...d, senha: e.target.value })} autoComplete={modo === 'entrar' ? 'current-password' : 'new-password'} /></Campo>
          {modo === 'registrar' && (
            <>
              <Campo rotulo="Telefone (opcional)"><input className={classeEntrada} value={d.telefone} onChange={(e) => setD({ ...d, telefone: e.target.value })} autoComplete="tel" /></Campo>
              <Campo rotulo="Município"><select className={classeEntrada} value={d.municipioCodigo} onChange={(e) => setD({ ...d, municipioCodigo: e.target.value })}><option value="">Prefiro não informar</option>{Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}</select></Campo>
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
      <Cartao titulo="Modo demonstração">
        <p className="mb-3 text-[14px] text-tinta-suave">Contas fictícias pré-cadastradas para apresentar os fluxos. Nenhuma corresponde a pessoa ou órgão real.</p>
        <ul className="grid gap-2">
          {PERFIS_DEMO.map((p) => (
            <li key={p.perfil}>
              <button onClick={() => executar(() => entrarDemo(p.perfil))} className="w-full rounded-controle bg-areia px-3 py-2.5 text-left ring-1 ring-transparent hover:ring-oceano" data-perfil={p.perfil}>
                <span className="block text-[15px] font-semibold text-petroleo">{p.rotulo}</span>
                <span className="block text-[13px] text-tinta-suave">{p.descricao}</span>
              </button>
            </li>
          ))}
        </ul>
      </Cartao>
    </div>
  )
}

interface Integrante { apelido?: string | null; idade: number | null; mobilidadeReduzida: boolean; dependeAssistencia: boolean; usaEquipamentoMedico: boolean; necessidadesEspeciais?: string | null }
interface Local { id: string; apelido: string; latitude: number; longitude: number; municipioCodigo: string }
interface Notif { id: string; titulo: string; mensagem: string; criadaEm: string; resposta: string | null; avisoOficial: boolean; entregas: { canal: string; resultado: string; em: string }[] }

function PerfilLogado() {
  const { usuario, sair, recarregar } = useSessao()
  const u = usuario!
  const [perfil, setPerfil] = useState({ telefone: '', municipioCodigo: u.municipioCodigo ?? '', enderecoReferencia: u.enderecoReferencia ?? '', consenteAlertas: u.consenteAlertas, consenteLocalizacao: u.consenteLocalizacao, receberPush: u.receberPush })
  const [msg, setMsg] = useState<string | null>(null)
  const operador = ['OperadorCentral', 'Administrador', 'Organizacao'].includes(u.papel)

  const salvar = async () => {
    try { await api('/api/eu/perfil', { method: 'PUT', json: { nome: u.nome, ...perfil, municipioCodigo: perfil.municipioCodigo || null } }); await recarregar(); setMsg('Preferências salvas.') }
    catch (e) { setMsg(e instanceof Error ? e.message : 'Falha') }
  }

  return (
    <div className="grid gap-4 md:grid-cols-2">
      <div className="grid content-start gap-4">
        <Cartao titulo={u.nome} acao={<Botao variante="fantasma" onClick={sair}>Sair</Botao>}>
          <p className="text-[14px]">{u.email}</p>
          <div className="mt-2 flex flex-wrap gap-2"><Selo>{u.papel}</Selo>{u.contaDemonstracao && <Selo tom="demo">Conta fictícia</Selo>}{u.centralCodigo && <Selo tom="espera">{u.centralCodigo}</Selo>}</div>
          {operador && (
            <div className="mt-3 flex flex-wrap gap-2">
              <Link to="/painel" className="rounded-controle bg-petroleo px-3 py-2 text-[14px] font-semibold text-branco">Painel de coordenação</Link>
              {u.centralCodigo && <Link to={`/central/${u.centralCodigo}`} className="rounded-controle bg-branco px-3 py-2 text-[14px] font-semibold text-petroleo ring-1 ring-linha">Central receptora simulada</Link>}
              {u.papel === 'Administrador' && <Link to="/demo" className="rounded-controle bg-[#3E2F5E] px-3 py-2 text-[14px] font-semibold text-branco">Controle da demonstração</Link>}
            </div>
          )}
        </Cartao>
        <Cartao titulo="Preferências e consentimentos">
          <div className="grid gap-3">
            <Campo rotulo="Município"><select className={classeEntrada} value={perfil.municipioCodigo} onChange={(e) => setPerfil({ ...perfil, municipioCodigo: e.target.value })}><option value="">Não informado</option>{Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}</select></Campo>
            <Campo rotulo="Endereço de referência" dica="Sua residência. Em um pedido de socorro usamos a localização atual, não este endereço."><input className={classeEntrada} value={perfil.enderecoReferencia} onChange={(e) => setPerfil({ ...perfil, enderecoReferencia: e.target.value })} /></Campo>
            {([['consenteAlertas', 'Quero receber avisos para meus locais monitorados'], ['consenteLocalizacao', 'Autorizo usar minha localização nos pedidos'], ['receberPush', 'Receber notificações push neste aparelho']] as const).map(([k, r]) => (
              <label key={k} className="flex items-start gap-2 text-[14.5px]"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={perfil[k]} onChange={(e) => setPerfil({ ...perfil, [k]: e.target.checked })} />{r}</label>
            ))}
            <Botao onClick={salvar}>Salvar preferências</Botao>
            <AtivarPush habilitado={perfil.receberPush && perfil.consenteAlertas} />
            {msg && <p role="status" className="text-[14px] text-seguro">{msg}</p>}
          </div>
        </Cartao>
        <Notificacoes />
      </div>
      <div className="grid content-start gap-4">
        <Familia />
        <LocaisMonitorados />
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
  const { dados, recarregar } = useDados<Local[]>('/api/eu/locais')
  const { posicao, iniciar } = useLocalizacao()
  const [apelido, setApelido] = useState('')
  const [erro, setErro] = useState<string | null>(null)
  const adicionar = async () => {
    if (!posicao) { iniciar(); return }
    try { await api('/api/eu/locais', { method: 'POST', json: { apelido: apelido || 'Local atual', latitude: posicao.latitude, longitude: posicao.longitude } }); setApelido(''); setErro(null); void recarregar() }
    catch (e) { setErro(e instanceof Error ? e.message : 'Falha') }
  }
  return (
    <Cartao titulo="Locais monitorados">
      <p className="mb-3 text-[13.5px] text-tinta-suave">Quando houver aviso para um destes locais, perguntamos se você e sua família estão bem.</p>
      {(dados ?? []).length === 0 ? <Vazio>Nenhum local monitorado.</Vazio> : (
        <ul className="grid gap-2">
          {dados!.map((l) => (
            <li key={l.id} className="flex items-center justify-between rounded-controle bg-areia px-3 py-2">
              <span><span className="font-semibold">{l.apelido}</span><span className="block text-[13px] text-tinta-suave">{nomeMunicipio(l.municipioCodigo)}</span></span>
              <Botao variante="fantasma" onClick={async () => { await api(`/api/eu/locais/${l.id}`, { method: 'DELETE' }); void recarregar() }}>Remover</Botao>
            </li>
          ))}
        </ul>
      )}
      <div className="mt-3 grid grid-cols-[1fr_auto] gap-2">
        <input className={classeEntrada} placeholder="Nome do local, ex.: casa da minha mãe" value={apelido} onChange={(e) => setApelido(e.target.value)} />
        <Botao onClick={adicionar}>{posicao ? 'Adicionar onde estou' : 'Usar localização'}</Botao>
      </div>
      {erro && <p role="alert" className="mt-2 text-[14px] text-emergencia-escura">{erro}</p>}
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
