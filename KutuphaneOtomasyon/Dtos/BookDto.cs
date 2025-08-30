namespace KutuphaneOtomasyon.Models.Dtos
{
    public enum BookSource { Local, Google, OpenLibrary }

    public class BookDto
    {
        public string Id { get; set; }                 // Kaynak içi id (bizde BookId, Google'da volumeId vb.)
        public string Isbn13 { get; set; }             // 13 haneli
        public string Isbn10 { get; set; }             // 10 haneli (varsa)
        public string Isbn => Isbn13 ?? Isbn10;        // front-end kolaylığı için

        public string Title { get; set; }
        public string Author { get; set; }
        public string Publisher { get; set; }
        public string Category { get; set; }
        public string PublishedDate { get; set; }
        public string Language { get; set; }
        public int? PageCount { get; set; }
        public string ThumbnailUrl { get; set; }
        public string Description { get; set; }

        public BookSource Source { get; set; }         // Local / Google / OpenLibrary
    }
}
