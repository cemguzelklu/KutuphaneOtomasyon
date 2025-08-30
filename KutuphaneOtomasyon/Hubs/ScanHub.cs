using Microsoft.AspNetCore.SignalR;

namespace YourApp.Hubs
{
    public class ScanHub : Hub
    {
        public Task Join(string token) =>
            Groups.AddToGroupAsync(Context.ConnectionId, token);

        public Task SendIsbn(string token, string isbn) =>
            Clients.Group(token).SendAsync("isbn", isbn);
    }
}
