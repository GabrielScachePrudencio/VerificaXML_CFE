using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VerificarDeXMLNFCE
{
    /// <summary>
    /// Consulta o portal público da NFC-e/NF-e no SEFAZ usando a chave de acesso.
    ///
    /// Estratégia:
    ///   1. Identifica o estado pelo cUF (posições 0-1 da chave).
    ///   2. Monta a URL do portal estadual correspondente.
    ///   3. Faz GET/POST simulando um browser para obter o HTML de resposta.
    ///   4. Extrai a data de pagamento com regex no HTML retornado.
    ///
    /// IMPORTANTE: Portais com CAPTCHA (reCAPTCHA) não podem ser consultados
    /// automaticamente. Nesses casos o status é marcado como "SemPagamento" com
    /// observação explicativa, cabendo ao usuário consultar manualmente.
    /// </summary>
    public static class SefazConsulta
    {
        // ─── HttpClient singleton (reutilizável) ─────────────────────────────────
        private static readonly HttpClientHandler _handler = new HttpClientHandler
        {
            AllowAutoRedirect       = true,
            UseCookies              = true,
            CookieContainer         = new CookieContainer(),
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        private static readonly HttpClient _http = new(_handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static SefazConsulta()
        {
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _http.DefaultRequestHeaders.Add("Accept-Language", "pt-BR,pt;q=0.9");
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  Método principal
        // ═════════════════════════════════════════════════════════════════════════
        public static async Task<ResultadoSefaz> ConsultarAsync(string chave44, CancellationToken ct)
        {
            if (chave44.Length != 44)
                return Falha("Chave de acesso inválida (deve ter 44 dígitos).");

            string cUF = chave44[..2];
            var portal = ResolverPortal(cUF);

            if (portal == null)
                return Falha($"Estado (cUF={cUF}) sem URL de consulta pública mapeada.");

            try
            {
                return portal.UsaPost
                    ? await ConsultarViaPost(chave44, portal, ct)
                    : await ConsultarViaGet(chave44, portal, ct);
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Falha($"Erro na consulta: {ex.Message}");
            }
        }

        // ─── GET simples ─────────────────────────────────────────────────────────
        private static async Task<ResultadoSefaz> ConsultarViaGet(
            string chave, PortalInfo portal, CancellationToken ct)
        {
            string url = string.Format(portal.UrlTemplate, chave);
            var resp   = await _http.GetAsync(url, ct);
            string html = await resp.Content.ReadAsStringAsync(ct);
            return ExtrairResultado(html, portal);
        }

        // ─── POST (portais que exigem form-submit) ───────────────────────────────
        private static async Task<ResultadoSefaz> ConsultarViaPost(
            string chave, PortalInfo portal, CancellationToken ct)
        {
            // Primeiro GET para pegar cookies/viewState
            string urlBase = portal.UrlBase!;
            var getResp    = await _http.GetAsync(urlBase, ct);
            string getHtml = await getResp.Content.ReadAsStringAsync(ct);

            // Extrai ViewState se existir
            string viewState = ExtractHidden(getHtml, "__VIEWSTATE");
            string vsGen     = ExtractHidden(getHtml, "__VIEWSTATEGENERATOR");
            string evVal     = ExtractHidden(getHtml, "__EVENTVALIDATION");

            var form = new Dictionary<string, string>
            {
                [portal.CampoChave!] = chave
            };
            if (!string.IsNullOrEmpty(viewState))  form["__VIEWSTATE"]           = viewState;
            if (!string.IsNullOrEmpty(vsGen))       form["__VIEWSTATEGENERATOR"]  = vsGen;
            if (!string.IsNullOrEmpty(evVal))       form["__EVENTVALIDATION"]     = evVal;
            if (portal.CamposExtras != null)
                foreach (var kv in portal.CamposExtras) form[kv.Key] = kv.Value;

            var content = new FormUrlEncodedContent(form);
            content.Headers.ContentType!.CharSet = "UTF-8";
            _http.DefaultRequestHeaders.Referrer = new Uri(urlBase);

            var postResp = await _http.PostAsync(urlBase, content, ct);
            string html  = await postResp.Content.ReadAsStringAsync(ct);
            return ExtrairResultado(html, portal);
        }

        // ─── Extração de data de pagamento do HTML ───────────────────────────────
        private static ResultadoSefaz ExtrairResultado(string html, PortalInfo portal)
        {
            if (string.IsNullOrWhiteSpace(html))
                return Falha("Resposta vazia do portal SEFAZ.");

            // Verifica se há CAPTCHA na página
            if (Regex.IsMatch(html, @"recaptcha|captcha|g-recaptcha", RegexOptions.IgnoreCase))
                return new ResultadoSefaz
                {
                    Status      = StatusConsulta.SemPagamento,
                    Observacao  = "Portal exige CAPTCHA — consulte manualmente no site da SEFAZ."
                };

            // Verifica erro de nota não encontrada
            if (Regex.IsMatch(html, @"(não encontrada|nao encontrada|not found|nota inválida|chave.*inválida)", RegexOptions.IgnoreCase))
                return new ResultadoSefaz
                {
                    Status     = StatusConsulta.SemPagamento,
                    Observacao = "Nota não encontrada no portal SEFAZ."
                };

            // Tenta extrair data de pagamento usando padrões comuns
            string? dataPgto = null;

            // Padrão 1: "Data Pagamento: dd/MM/yyyy"
            var m1 = Regex.Match(html,
                @"[Dd]ata\s+[Pp]agamento[:\s]+(\d{2}/\d{2}/\d{4}(?:\s+\d{2}:\d{2}(?::\d{2})?)?)");
            if (m1.Success) dataPgto = m1.Groups[1].Value.Trim();

            // Padrão 2: "Pagamento" em tabela HTML próximo a uma data
            if (dataPgto == null)
            {
                var m2 = Regex.Match(html,
                    @"[Pp]agamento.*?(\d{2}/\d{2}/\d{4}(?:\s+\d{2}:\d{2})?)",
                    RegexOptions.Singleline);
                if (m2.Success) dataPgto = m2.Groups[1].Value.Trim();
            }

            // Padrão 3: "data_pagamento" em JSON embutido
            if (dataPgto == null)
            {
                var m3 = Regex.Match(html,
                    @"""data_pagamento[_\w]*""\s*:\s*""(\d{2}/\d{2}/\d{4}(?:[\sT]\d{2}:\d{2})?)""");
                if (m3.Success) dataPgto = m3.Groups[1].Value.Trim();
            }

            // Padrão 4: Procura "vPag" ou valor próximo a "Pagamento" no XML embutido
            if (dataPgto == null)
            {
                var m4 = Regex.Match(html,
                    @"<dhPag[^>]*>(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[^<]*)</dhPag>",
                    RegexOptions.IgnoreCase);
                if (m4.Success && DateTime.TryParse(m4.Groups[1].Value, out var dtIso))
                    dataPgto = dtIso.ToString("dd/MM/yyyy HH:mm");
            }

            if (!string.IsNullOrEmpty(dataPgto))
            {
                return new ResultadoSefaz
                {
                    Status         = StatusConsulta.ComPagamento,
                    DataPagamento  = dataPgto,
                    Observacao     = $"Consultado via {portal.Nome}"
                };
            }

            // Verifica se a nota está autorizada mas sem data de pagamento explícita
            bool autorizada = Regex.IsMatch(html,
                @"(autoriza|Autorizada|AUTORIZADA|100|uso autorizado)", RegexOptions.IgnoreCase);

            return new ResultadoSefaz
            {
                Status    = StatusConsulta.SemPagamento,
                Observacao = autorizada
                    ? $"Nota autorizada, mas sem data de pagamento registrada — {portal.Nome}"
                    : $"Data de pagamento não localizada no portal {portal.Nome}"
            };
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────
        private static ResultadoSefaz Falha(string msg) =>
            new() { Status = StatusConsulta.Erro, Observacao = msg };

        private static string ExtractHidden(string html, string name)
        {
            var m = Regex.Match(html,
                $@"<input[^>]+name=""{Regex.Escape(name)}""[^>]+value=""([^""]*)""",
                RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "";
        }

        // ═════════════════════════════════════════════════════════════════════════
        //  Mapeamento de estados → portais
        // ═════════════════════════════════════════════════════════════════════════
        private static PortalInfo? ResolverPortal(string cuf) => cuf switch
        {
            // AC - Acre
            "12" => new PortalInfo("AC/SEFAZ",
                        "https://www.sefaznet.ac.gov.br/nfce/consulta?chave={0}", false),

            // AL - Alagoas
            "27" => new PortalInfo("AL/SEFAZ",
                        "https://nfce.sefaz.al.gov.br/consultanfce.htm?chave={0}", false),

            // AM - Amazonas
            "13" => new PortalInfo("AM/SEFAZ",
                        "https://sistemas.sefaz.am.gov.br/nfceweb/formConsulta.do?chave={0}", false),

            // AP - Amapá
            "16" => new PortalInfo("AP/SEFAZ",
                        "https://www.sefaz.ap.gov.br/nfce/nfceweb/formConsulta.do?chave={0}", false),

            // BA - Bahia
            "29" => new PortalInfo("BA/SEFAZ",
                        "https://nfe.sefaz.ba.gov.br/servicos/nfce/default.aspx?chave={0}", false),

            // CE - Ceará
            "23" => new PortalInfo("CE/SEFAZ",
                        "http://nfce.sefaz.ce.gov.br/pages/showNFCe.html?chave={0}&tpAmb=1", false),

            // DF - Distrito Federal
            "53" => new PortalInfo("DF/SEF",
                        "https://www.fazenda.df.gov.br/nfce/danfce?chave={0}", false),

            // ES - Espírito Santo
            "32" => new PortalInfo("ES/SEFAZ",
                        "https://app.sefaz.es.gov.br/ConsultaNFCe/qrCode.aspx?p={0}", false),

            // GO - Goiás
            "52" => new PortalInfo("GO/SEFAZ",
                        "https://nfe.sefaz.go.gov.br/nfeweb/sites/nfce/danfeNFCe.do?chave={0}", false),

            // MA - Maranhão
            "21" => new PortalInfo("MA/SEFAZ",
                        "https://www.nfe.sefaz.ma.gov.br/nfceweb/formConsulta.do?chave={0}", false),

            // MG - Minas Gerais
            "31" => new PortalInfo("MG/SEF",
                        "https://portalsped.fazenda.mg.gov.br/portalnfce/sistema/qrcode.xhtml?p={0}&tpAmb=1", false),

            // MS - Mato Grosso do Sul
            "50" => new PortalInfo("MS/SEFAZ",
                        "https://www.dfe.ms.gov.br/nfce/qrcode?chave={0}", false),

            // MT - Mato Grosso
            "51" => new PortalInfo("MT/SEFAZ",
                        "https://www.sefaz.mt.gov.br/nfce/consultanfce?chave={0}", false),

            // PA - Pará
            "15" => new PortalInfo("PA/SEFA",
                        "https://appnfce.sefa.pa.gov.br:8080/nfceweb/formConsulta.do?chave={0}", false),

            // PB - Paraíba
            "25" => new PortalInfo("PB/SEFAZ",
                        "https://www.sefaz.pb.gov.br/nfce/consulta?p={0}", false),

            // PE - Pernambuco
            "26" => new PortalInfo("PE/SEFAZ",
                        "https://nfce.sefaz.pe.gov.br/nfce/consulta?chave={0}", false),

            // PI - Piauí
            "22" => new PortalInfo("PI/SEFAZ",
                        "https://www.sefaz.pi.gov.br/nfce/consulta?p={0}", false),

            // PR - Paraná
            "41" => new PortalInfo("PR/SEFA",
                        "https://www.fazenda.pr.gov.br/nfce/qrcode?p={0}", false),

            // RJ - Rio de Janeiro
            "33" => new PortalInfo("RJ/SEFAZ",
                        "https://consultadfe.fazenda.rj.gov.br/consultaDFe/paginas/consultaChaveAcesso.faces",
                        true,
                        urlBase: "https://consultadfe.fazenda.rj.gov.br/consultaDFe/paginas/consultaChaveAcesso.faces",
                        campoChave: "form:j_idt15",
                        camposExtras: new Dictionary<string, string>
                        {
                            ["form_SUBMIT"] = "1",
                            ["javax.faces.ViewState"] = "j_id1"
                        }),

            // RN - Rio Grande do Norte
            "24" => new PortalInfo("RN/SET",
                        "http://nfce.set.rn.gov.br/consultarNFCe.aspx?chave={0}", false),

            // RO - Rondônia
            "11" => new PortalInfo("RO/SEFIN",
                        "https://www.nfce.sefin.ro.gov.br/consultanfce/consulta.jsp?chave={0}", false),

            // RR - Roraima
            "14" => new PortalInfo("RR/SEFAZ",
                        "https://nfce.sefaz.rr.gov.br/nfceweb/formConsulta.do?chave={0}", false),

            // RS - Rio Grande do Sul
            "43" => new PortalInfo("RS/SEFAZ",
                        "https://www.nfe.se.gov.br/nfce/consulta/consultar_nfce.asp?chave={0}", false),

            // SC - Santa Catarina
            "42" => new PortalInfo("SC/SEF",
                        "https://sat.sef.sc.gov.br/tax.NET/Sat.NFe.Web/Consultas/ConsultaPublicaNFCe.aspx?chave={0}", false),

            // SE - Sergipe
            "28" => new PortalInfo("SE/SEFAZ",
                        "https://www.nfe.se.gov.br/nfce/consulta/consultar_nfce.asp?chave={0}", false),

            // SP - São Paulo
            "35" => new PortalInfo("SP/SEFAZ",
                        "https://www.nfce.fazenda.sp.gov.br/NFCeConsultaPublica/Paginas/ConsultaPublica.aspx",
                        true,
                        urlBase: "https://www.nfce.fazenda.sp.gov.br/NFCeConsultaPublica/Paginas/ConsultaPublica.aspx",
                        campoChave: "ctl00$Conteudo$txtChaveAcesso",
                        camposExtras: new Dictionary<string, string>
                        {
                            ["ctl00$Conteudo$btnConsultar"] = "Consultar"
                        }),

            // TO - Tocantins
            "17" => new PortalInfo("TO/SEFAZ",
                        "https://www.sefaz.to.gov.br/nfce/consulta.jsf?chave={0}", false),

            _ => null
        };

        // ─── Classe auxiliar de portal ────────────────────────────────────────────
        private sealed class PortalInfo
        {
            public string Nome         { get; }
            public string UrlTemplate  { get; }
            public bool   UsaPost      { get; }
            public string? UrlBase     { get; }
            public string? CampoChave  { get; }
            public Dictionary<string, string>? CamposExtras { get; }

            public PortalInfo(string nome, string urlTemplate, bool usaPost,
                string? urlBase = null, string? campoChave = null,
                Dictionary<string, string>? camposExtras = null)
            {
                Nome         = nome;
                UrlTemplate  = urlTemplate;
                UsaPost      = usaPost;
                UrlBase      = urlBase;
                CampoChave   = campoChave;
                CamposExtras = camposExtras;
            }
        }
    }
}
