namespace KutuphaneOtomasyon.Models.Dtos
{
    public class FederatedResponse
    {
        public BookDto Local { get; set; }
        public BookDto Google { get; set; }
        public BookDto OpenLibrary { get; set; }
    }
}
