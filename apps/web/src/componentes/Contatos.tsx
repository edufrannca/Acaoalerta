const CONTATOS = [
  { numero: '193', nome: 'Bombeiros', uso: 'Resgate, ilhamento, desabamento' },
  { numero: '199', nome: 'Defesa Civil', uso: 'Deslizamento, alagamento, vistoria' },
  { numero: '192', nome: 'SAMU', uso: 'Urgência médica' },
  { numero: '190', nome: 'Polícia Militar', uso: 'Segurança pública' },
]

/** Ligação iniciada apenas por toque explícito. O app não acompanha o resultado da chamada. */
export function ContatosEmergencia({ compacto = false, destaque }: { compacto?: boolean; destaque?: string[] }) {
  const lista = destaque ? CONTATOS.filter((c) => destaque.includes(c.numero)) : CONTATOS
  return (
    <div>
      <ul className={`grid gap-2 ${compacto ? 'grid-cols-2' : 'grid-cols-2 sm:grid-cols-4'}`}>
        {lista.map((c) => (
          <li key={c.numero}>
            <a href={`tel:${c.numero}`} className="flex min-h-14 items-center gap-3 rounded-controle bg-branco px-3 py-2 ring-1 ring-linha hover:ring-oceano">
              <span className="numero-tabular text-[22px] font-bold text-emergencia">{c.numero}</span>
              <span className="leading-tight">
                <span className="block text-[14px] font-semibold text-petroleo">{c.nome}</span>
                {!compacto && <span className="block text-[12px] text-tinta-suave">{c.uso}</span>}
              </span>
            </a>
          </li>
        ))}
      </ul>
      <p className="mt-2 text-[12.5px] text-tinta-suave">A ligação sai do seu telefone. O aplicativo não sabe se ela foi atendida.</p>
    </div>
  )
}
