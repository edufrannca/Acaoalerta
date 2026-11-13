export const MUNICIPIOS: Record<string, string> = {
  '2611606': 'Recife',
  '2607901': 'Jaboatão dos Guararapes',
  '2609600': 'Olinda',
  '2610707': 'Paulista',
  '2603454': 'Camaragibe',
  '2613701': 'São Lourenço da Mata',
  '2602902': 'Cabo de Santo Agostinho',
  '2607208': 'Ipojuca',
  '2609402': 'Moreno',
  '2600054': 'Abreu e Lima',
  '2606804': 'Igarassu',
  '2607752': 'Itapissuma',
  '2607604': 'Ilha de Itamaracá',
  '2601052': 'Araçoiaba',
}

export const nomeMunicipio = (c?: string | null) => (c ? MUNICIPIOS[c] ?? c : 'Município não identificado')

export const TIPOS_AJUDA: Record<string, string> = {
  DoacaoAgua: 'Doação de água',
  DoacaoAlimentos: 'Doação de alimentos',
  Roupas: 'Roupas',
  MedicamentosViaCanalAdequado: 'Medicamentos (por canal adequado)',
  KitHigiene: 'Kits de higiene',
  ApoioEmAbrigo: 'Apoio em abrigo',
  ApoioLogisticoSeguro: 'Apoio logístico seguro',
  VoluntariadoLocalVerificado: 'Voluntariado em local verificado',
}

export const STATUS_ENCAMINHAMENTO: Record<string, { rotulo: string; tom: 'ok' | 'espera' | 'erro' }> = {
  Pendente: { rotulo: 'Aguardando envio', tom: 'espera' },
  Transmitindo: { rotulo: 'Transmitindo', tom: 'espera' },
  Recebido: { rotulo: 'Recebimento confirmado', tom: 'ok' },
  FalhaTemporaria: { rotulo: 'Falha; nova tentativa agendada', tom: 'erro' },
  Rejeitado: { rotulo: 'Recusado pela central', tom: 'erro' },
  Esgotado: { rotulo: 'Tentativas esgotadas', tom: 'erro' },
}
