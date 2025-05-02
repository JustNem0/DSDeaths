using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;


namespace DSDeaths
{
    class Game
    {
        public readonly string name;
        public readonly int[] offsets32;
        public readonly int[] offsets64;

        public Game(in string name, in int[] offsets32, in int[] offsets64)
        {
            this.name = name;
            this.offsets32 = offsets32;
            this.offsets64 = offsets64;
        }
    }

    class Program
    {
        const int PROCESS_WM_READ = 0x0010;
        const int PROCESS_QUERY_INFORMATION = 0x0400;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool IsWow64Process(IntPtr hProcess, ref bool Wow64Process);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadProcessMemory(
            IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        static readonly Game[] games =
        {
            new Game("DARKSOULS", new int[] { 0xF78700, 0x5C }, null),
            new Game("DarkSoulsII", new int[] { 0x1150414, 0x74, 0xB8, 0x34, 0x4, 0x28C, 0x100 },
                new int[] { 0x16148F0, 0xD0, 0x490, 0x104 }),
            new Game("DarkSoulsIII", null, new int[] { 0x47572B8, 0x98 }),
            new Game("DarkSoulsRemastered", null, new int[] { 0x1C8A530, 0x98 }),
            new Game("Sekiro", null, new int[] { 0x3D5AAC0, 0x90 }),
            new Game("eldenring", null, new int[] { 0x3D5DF38, 0x94 }),
            new Game("LOP-Win64-Shipping", null, new int[] { 0x07196928, 0x98, 0x110, 0xEE0, 0xA0, 0xDC8, 0x98 })
        };

        static bool Write(string gameName, int value)
        {
            try
            {
                string fileName = gameName + ".txt";
                File.WriteAllText(fileName, value.ToString());
            }
            catch (IOException)
            {
                Console.WriteLine("Could not write to file for " + gameName);
                return false;
            }

            return true;
        }

        static void CreateFileIfNotExist(string gameName)
        {
            if (File.Exists(gameName + ".txt"))
            {
                return;
            }

            Write(gameName, 0);
        }

        static bool PeekMemory(IntPtr handle, IntPtr baseAddress, bool isX64, int[] offsets, ref int value)
        {
            long address = baseAddress.ToInt64();
            byte[] buffer = new byte[8];
            int discard = 0;

            foreach (int offset in offsets)
            {
                if (address == 0) return false;
                address += offset;
                if (!ReadProcessMemory(handle, (IntPtr)address, buffer, 8, ref discard))
                {
                    //Console.WriteLine("Could not read game memory.");
                    return false;
                }

                address = isX64 ? BitConverter.ToInt64(buffer, 0) : BitConverter.ToInt32(buffer, 0);
            }

            value = (int)address;
            return true;
        }

        static async Task MonitorGameAsync(Game game, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Process[] processes = Process.GetProcessesByName(game.name);
                if (processes.Length == 0)
                {
                    await Task.Delay(1000, token);
                    continue;
                }

                Process proc = processes[0];
                Console.WriteLine($"[{game.name}] Found process.");
                CreateFileIfNotExist(game.name);

                IntPtr handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_WM_READ, false, proc.Id);
                IntPtr baseAddress = proc.MainModule.BaseAddress;
                bool isWow64 = false;

                if (!IsWow64Process(handle, ref isWow64))
                {
                    Console.WriteLine($"[{game.name}] Could not determine architecture.");
                    await Task.Delay(1000, token);
                    continue;
                }

                int[] offsets = isWow64 ? game.offsets32 : game.offsets64;
                if (offsets == null)
                {
                    Console.WriteLine($"[{game.name}] No offsets available for this architecture.");
                    return;
                }

                int oldValue = 0;
                Write(game.name, 0);

                while (!proc.HasExited && !token.IsCancellationRequested)
                {
                    int value = 0;
                    if (PeekMemory(handle, baseAddress, !isWow64, offsets, ref value))
                    {
                        if (value != oldValue)
                        {
                            oldValue = value;
                            Write(game.name, value);
                            Console.WriteLine($"[{game.name}] Deaths: {value}");
                        }
                    }

                    await Task.Delay(500, token);
                }

                Console.WriteLine($"[{game.name}] Process exited.");
                await Task.Delay(2000, token); // Give time before looking again
            }
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("-----------------------------------WARNING-----------------------------------");
            Console.WriteLine(" Does NOT work with Elden Ring if Easy Anti-Cheat (EAC) is running.");
            Console.WriteLine(" Possible risk of BANS by trying to use with EAC enabled.");
            Console.WriteLine(" USE AT YOUR OWN RISK.");
            Console.WriteLine("-----------------------------------WARNING-----------------------------------\n");
            Console.WriteLine("                             GLORY TO UKRAINE!!!!                            \n");

            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            CancellationTokenSource cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Shutting down...");
            };

            var tasks = games.Select(game => MonitorGameAsync(game, cts.Token));
            await Task.WhenAll(tasks);
        }
    }
}