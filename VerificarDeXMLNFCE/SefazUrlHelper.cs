namespace VerificarDeXMLNFCE
{
    /// <summary>
    /// Monta a URL pública de consulta de NF-e/NFC-e por estado
    /// para abrir no navegador do usuário.
    /// </summary>
    public static class SefazUrlHelper
    {
        public static string ObterUrlConsulta(string cuf, string chave44)
        {
            return cuf switch
            {
                "12" => $"https://www.sefaznet.ac.gov.br/nfce/consulta?chave={chave44}",
                "27" => $"https://nfce.sefaz.al.gov.br/consultanfce.htm?chave={chave44}",
                "13" => $"https://sistemas.sefaz.am.gov.br/nfceweb/formConsulta.do?chave={chave44}",
                "16" => $"https://www.sefaz.ap.gov.br/nfce/nfceweb/formConsulta.do?chave={chave44}",
                "29" => $"https://nfe.sefaz.ba.gov.br/servicos/nfce/default.aspx?chave={chave44}",
                "23" => $"http://nfce.sefaz.ce.gov.br/pages/showNFCe.html?chave={chave44}&tpAmb=1",
                "53" => $"https://www.fazenda.df.gov.br/nfce/danfce?chave={chave44}",
                "32" => $"https://app.sefaz.es.gov.br/ConsultaNFCe/qrCode.aspx?p={chave44}",
                "52" => $"https://nfe.sefaz.go.gov.br/nfeweb/sites/nfce/danfeNFCe.do?chave={chave44}",
                "21" => $"https://www.nfe.sefaz.ma.gov.br/nfceweb/formConsulta.do?chave={chave44}",
                "31" => $"https://portalsped.fazenda.mg.gov.br/portalnfce/sistema/qrcode.xhtml?p={chave44}&tpAmb=1",
                "50" => $"https://www.dfe.ms.gov.br/nfce/qrcode?chave={chave44}",
                "51" => $"https://www.sefaz.mt.gov.br/nfce/consultanfce?chave={chave44}",
                "15" => $"https://appnfce.sefa.pa.gov.br:8080/nfceweb/formConsulta.do?chave={chave44}",
                "25" => $"https://www.sefaz.pb.gov.br/nfce/consulta?p={chave44}",
                "26" => $"https://nfce.sefaz.pe.gov.br/nfce/consulta?chave={chave44}",
                "22" => $"https://www.sefaz.pi.gov.br/nfce/consulta?p={chave44}",
                "41" => $"https://www.fazenda.pr.gov.br/nfce/qrcode?p={chave44}",
                "33" => $"https://consultadfe.fazenda.rj.gov.br/consultaDFe/paginas/consultaChaveAcesso.faces",
                "24" => $"http://nfce.set.rn.gov.br/consultarNFCe.aspx?chave={chave44}",
                "11" => $"https://www.nfce.sefin.ro.gov.br/consultanfce/consulta.jsp?chave={chave44}",
                "14" => $"https://nfce.sefaz.rr.gov.br/nfceweb/formConsulta.do?chave={chave44}",
                "43" => $"https://www.nfe.se.gov.br/nfce/consulta/consultar_nfce.asp?chave={chave44}",
                "42" => $"https://sat.sef.sc.gov.br/tax.NET/Sat.NFe.Web/Consultas/ConsultaPublicaNFCe.aspx?chave={chave44}",
                "28" => $"https://www.nfe.se.gov.br/nfce/consulta/consultar_nfce.asp?chave={chave44}",
                "35" => $"https://www.nfce.fazenda.sp.gov.br/NFCeConsultaPublica/Paginas/ConsultaPublica.aspx",
                "17" => $"https://www.sefaz.to.gov.br/nfce/consulta.jsf?chave={chave44}",
                // Portal nacional como fallback
                _    => $"https://www.nfe.fazenda.gov.br/portal/consultaRecaptcha.aspx?tipoConsulta=resumo&tipoConteudo=7PhJ+gAVw2g="
            };
        }
    }
}
