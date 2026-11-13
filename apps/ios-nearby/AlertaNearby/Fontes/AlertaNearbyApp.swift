import CoreLocation
import SwiftUI

@main
struct AlertaNearbyApp: App {
    @StateObject private var servico = ServicoProximidade()
    private let localizacao = CLLocationManager()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(servico)
                // iOS 13+: sem autorização de localização, o iOS rejeita conexões rápidas por Wi-Fi Hotspot (orientação do SDK).
                .onAppear {
                    localizacao.requestWhenInUseAuthorization()
                    let gerente = localizacao
                    servico.posicaoDesteAparelho = {
                        guard let l = gerente.location, l.horizontalAccuracy >= 0, Date().timeIntervalSince(l.timestamp) < 600 else { return nil }
                        return ["latitude": (l.coordinate.latitude * 1e5).rounded() / 1e5, "longitude": (l.coordinate.longitude * 1e5).rounded() / 1e5,
                                "precisaoMetros": l.horizontalAccuracy.rounded()]
                    }
                }
                // Pedido criado na PWA deste iPhone: alertaemacao://importar#p=…
                .onOpenURL { servico.importarDaPwa($0) }
        }
    }
}

struct ContentView: View {
    @EnvironmentObject var s: ServicoProximidade
    @State private var config = Configuracao.carregar()
    @State private var mostrarExportacao = false

    var body: some View {
        NavigationView {
            List {
                Section {
                    Text("AMBIENTE DE DEMONSTRAÇÃO — dados fictícios. Nenhum órgão real é acionado. Em emergência real ligue 193, 199, 192 ou 190.")
                        .font(.callout.weight(.semibold)).foregroundColor(.white).padding(8)
                        .listRowBackground(Color(red: 0.66, green: 0.20, blue: 0.20))
                }
                Section(header: Text("Estado")) {
                    Text(s.identificacao).font(.footnote)
                    Text("Internet: \(s.internet ? "disponível" : "INDISPONÍVEL") · Visível: \(s.anunciando ? "sim" : "não") · Procurando: \(s.procurando ? "sim" : "não")")
                    Text("Conectados: \(s.conectados.isEmpty ? "nenhum" : s.conectados.values.joined(separator: ", "))")
                    Text("Pendentes de envio ao servidor: \(s.repo.pendentesGateway().count)")
                }
                Section(header: Text("Configuração")) {
                    TextField("Nome deste aparelho (ex.: iPhone A)", text: $config.nome)
                    TextField("Código do grupo de demonstração", text: $config.codigoGrupo).autocapitalization(.allCharacters)
                    TextField("URL do servidor (https://…)", text: $config.urlServidor).keyboardType(.URL).autocapitalization(.none)
                    TextField("Identificador do gateway", text: $config.receptor).autocapitalization(.none)
                    SecureField("Chave do gateway (tela /demo do servidor)", text: $config.chaveGateway)
                    Toggle("Atuar como gateway", isOn: $config.atuarComoGateway)
                    Button("Salvar configuração") { config.salvar(); s.configuracao = config; s.atualizar() }
                }
                Section(header: Text("Comunicação")) {
                    botao(s.anunciando ? "Parar de receber" : "Receber: tornar este aparelho visível") { s.alternarAnuncio() }
                    botao(s.procurando ? "Parar de procurar" : "Enviar: procurar aparelhos próximos") { s.alternarDescoberta() }
                    botao("Criar pedido de socorro FICTÍCIO") { s.criarPedidoFicticio() }
                    botao("Enviar aviso demonstrativo a todos os conectados") { s.enviarAvisoDemonstrativo() }
                    botao("Encaminhar pendentes ao servidor agora") { s.encaminharPendentes() }
                    botao("Exportar evidências (sem chaves)") { mostrarExportacao = true }
                    botao("Parar tudo e desconectar") { s.pararTudo() }
                }
                Section(header: Text("Aparelhos encontrados"), footer: Text("Descoberta não significa entrega: a entrega só vale com a confirmação do receptor.")) {
                    if s.encontrados.isEmpty { Text("Nenhum aparelho encontrado ainda.") }
                    ForEach(Array(s.encontrados.keys).sorted(), id: \.self) { id in
                        let nome = s.encontrados[id] ?? id
                        if s.conectados[id] != nil { Text("\(nome) — conectado") }
                        else { botao("Conectar a \(nome)") { s.conectar(id) } }
                    }
                }
                Section(header: Text("Mensagens e confirmações")) {
                    ForEach(s.registros, id: \.id) { r in
                        VStack(alignment: .leading, spacing: 4) {
                            Text("\(r.direcao == "enviada" ? "ENVIADA" : "RECEBIDA") · \(r.tipo)").font(.headline)
                            Text("Mensagem \(String(r.id.prefix(8))) · Ocorrência \(String(r.ocorrencia.prefix(8)))").font(.footnote)
                            Text(Protocolo.Estado.descrever(r.nivel)).font(.subheadline.weight(.semibold))
                            if let p = r.protocolos { Text("Protocolos (SIMULADOS): \(p)").font(.footnote) }
                            if let e = r.ultimoErro { Text("Último erro: \(e)").font(.footnote).foregroundColor(.red) }
                            ForEach(Array(r.eventos.enumerated()), id: \.offset) { _, ev in
                                Text("\(String((ev["em"] ?? "").dropFirst(11).prefix(12)))  \(ev["texto"] ?? "")").font(.caption)
                            }
                        }.accessibilityElement(children: .combine)
                    }
                }
                Section(header: Text("Registro")) {
                    ForEach(Array(s.log.enumerated()), id: \.offset) { _, l in Text(l).font(.caption2) }
                }
            }
            .navigationTitle("Alerta em Ação — Proximidade")
        }
        .navigationViewStyle(.stack)
        .alert(item: $s.verificacao) { v in
            Alert(title: Text("Confirme o pareamento"),
                  message: Text("Aparelho: \(v.nome)\n\nCódigo de verificação: \(v.codigo)\n\nO OUTRO aparelho deve mostrar exatamente o mesmo código. Se não mostrar, ou se você não reconhece o aparelho, recuse."),
                  primaryButton: .default(Text("Os códigos conferem — conectar")) { v.responder(true) },
                  secondaryButton: .destructive(Text("Recusar")) { v.responder(false) })
        }
        .sheet(isPresented: $mostrarExportacao) {
            CompartilharTexto(texto: s.repo.exportarEvidencias())
        }
        .overlay(Group {
            if let a = s.alerta {
                VStack {
                    Spacer()
                    Text(a).padding().background(Color(.systemBackground)).cornerRadius(12).shadow(radius: 4).padding()
                        .onTapGesture { s.alerta = nil }
                        .accessibilityAddTraits(.isButton).accessibilityHint("Toque para fechar")
                }
            }
        })
    }

    private func botao(_ titulo: String, _ acao: @escaping () -> Void) -> some View {
        Button(action: acao) { Text(titulo).font(.body.weight(.semibold)).frame(maxWidth: .infinity, minHeight: 44, alignment: .leading) }
    }
}

struct CompartilharTexto: UIViewControllerRepresentable {
    let texto: String
    func makeUIViewController(context: Context) -> UIActivityViewController { UIActivityViewController(activityItems: [texto], applicationActivities: nil) }
    func updateUIViewController(_ uiViewController: UIActivityViewController, context: Context) {}
}
