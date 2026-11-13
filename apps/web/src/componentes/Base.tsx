import type { ReactNode } from 'react'

export function Marca({ tamanho = 32 }: { tamanho?: number }) {
  return (
    <svg width={tamanho} height={tamanho} viewBox="0 0 64 64" aria-hidden="true">
      <rect width="64" height="64" rx="14" fill="#193B44" />
      <path d="M32 10 L50 17 V31 C50 42 42 50 32 54 C22 50 14 42 14 31 V17 Z" fill="none" stroke="#F8F5EF" strokeWidth="3.5" strokeLinejoin="round" />
      <path d="M19 34 C23 30 27 30 31 34 C35 38 39 38 45 33" fill="none" stroke="#5FB3B0" strokeWidth="3.5" strokeLinecap="round" />
      <circle cx="32" cy="24" r="3.2" fill="#F8F5EF" />
    </svg>
  )
}

type Tom = 'neutro' | 'ok' | 'espera' | 'erro' | 'atencao' | 'demo'

const tons: Record<Tom, string> = {
  neutro: 'bg-areia-escura text-tinta',
  ok: 'bg-seguro-claro text-seguro',
  espera: 'bg-[#E4EEEE] text-oceano',
  erro: 'bg-emergencia-claro text-emergencia-escura',
  atencao: 'bg-atencao-claro text-atencao',
  demo: 'bg-[#E9E4F0] text-[#4A3A6B]',
}

export function Selo({ tom = 'neutro', children }: { tom?: Tom; children: ReactNode }) {
  return <span className={`inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-[13px] font-medium ${tons[tom]}`}>{children}</span>
}

export function Cartao({ children, className = '', titulo, acao }: { children: ReactNode; className?: string; titulo?: string; acao?: ReactNode }) {
  return (
    <section className={`rounded-cartao bg-branco p-4 sm:p-5 ring-1 ring-linha/70 ${className}`}>
      {titulo && (
        <header className="mb-3 flex items-baseline justify-between gap-3">
          <h2 className="text-[17px] font-semibold text-petroleo">{titulo}</h2>
          {acao}
        </header>
      )}
      {children}
    </section>
  )
}

export function Fonte({ nome, em, oficial, demonstrativa }: { nome: string; em?: string | null; oficial?: boolean; demonstrativa?: boolean }) {
  return (
    <p className="mt-1 text-[12.5px] text-tinta-suave">
      Fonte: {nome}
      {em ? `, atualizado em ${new Date(em).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone: 'America/Recife' })}` : ''}
      {demonstrativa ? ' (dado demonstrativo)' : oficial === false ? ' (não oficial)' : ''}
    </p>
  )
}

export function Botao({ children, variante = 'primario', className = '', ...p }: React.ButtonHTMLAttributes<HTMLButtonElement> & { variante?: 'primario' | 'secundario' | 'perigo' | 'seguro' | 'fantasma' }) {
  const v = {
    primario: 'bg-petroleo text-branco hover:bg-petroleo-900',
    secundario: 'bg-branco text-petroleo ring-1 ring-linha hover:bg-areia',
    perigo: 'bg-emergencia text-branco hover:bg-emergencia-escura',
    seguro: 'bg-seguro text-branco hover:brightness-95',
    fantasma: 'text-oceano hover:bg-areia-escura',
  }[variante]
  return (
    <button {...p} className={`inline-flex min-h-11 items-center justify-center gap-2 rounded-controle px-4 py-2 text-[15px] font-semibold transition-colors disabled:opacity-50 ${v} ${className}`}>
      {children}
    </button>
  )
}

export function Campo({ rotulo, dica, children }: { rotulo: string; dica?: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-[14px] font-medium text-tinta">{rotulo}</span>
      {children}
      {dica && <span className="mt-1 block text-[12.5px] text-tinta-suave">{dica}</span>}
    </label>
  )
}

export const classeEntrada = 'w-full rounded-controle border border-linha bg-branco px-3 py-2.5 text-[15px] text-tinta placeholder:text-tinta-suave/70 focus:border-oceano'

export function AvisoSimulado({ texto = 'Ambiente simulado. Nenhum órgão real foi acionado.' }: { texto?: string }) {
  return (
    <div role="note" className="rounded-controle border-2 border-dashed border-[#6E5A95] bg-[#F3F0F8] px-3 py-2 text-[14px] font-semibold text-[#3E2F5E]">
      {texto}
    </div>
  )
}

export function Vazio({ children }: { children: ReactNode }) {
  return <p className="rounded-controle bg-areia px-3 py-4 text-center text-[14px] text-tinta-suave">{children}</p>
}
