using System;
using System.Threading.Tasks;

namespace LANChat.Server
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ChatServer server = new ChatServer(5000);

           
            _ = Task.Run(() =>
            {
                Console.WriteLine("Нажмите 'q' для остановки сервера.");
                while (Console.ReadKey(true).KeyChar != 'q') { }
                server.Stop();
            });

            await server.StartAsync();
            Console.WriteLine("[SERVER] Сервер остановлен.");
        }
    }
}