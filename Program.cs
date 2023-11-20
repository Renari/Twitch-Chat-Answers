using System.Text.Json;
using System.Text.RegularExpressions;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;

namespace TwitchChatAnswers;

internal class Config
{
    public required string OAuthToken { get; set; }
    public required string Name { get; set; }
}

partial class Program
{
    private static TwitchClient? _client;
    private static readonly Dictionary<string, int> MessageCounts = new();
    private static readonly Dictionary<string, List<string>> UserMessages = new();
    private static readonly object LockObject = new();
    private static readonly object InputLockObject = new();
    private static DateTime _lastUpdateTime = DateTime.Now;
    private static readonly List<char> UserInputBuffer = new();
    private static bool _running = true;

    public static void Main()
    {
        var configFileContent = File.ReadAllText("config.json");
        var config = JsonSerializer.Deserialize<Config>(configFileContent);
        if (config == null)
        {
            WriteLine("Invalid Config");
            return;
        }

        var credentials = new ConnectionCredentials(config.Name, config.OAuthToken);
        var clientOptions = new ClientOptions
        {
            MessagesAllowedInPeriod = 100,
            ThrottlingPeriod = TimeSpan.FromSeconds(30),
        };
        
        _client = new TwitchClient(new WebSocketClient(clientOptions));
        _client.Initialize(credentials, config.Name);

        _client.OnMessageReceived += Client_OnMessageReceived;

        _client.Connect();

        ThreadPool.QueueUserWorkItem(ProcessInput);
        
        while (_running)
        {
            if (DateTime.Now - _lastUpdateTime > TimeSpan.FromSeconds(5))
            {
                _lastUpdateTime = DateTime.Now;
                UpdateOutputMessages();
            }
            Thread.Sleep(50);
        }
    }

    private static void Client_OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        var message = e.ChatMessage.Message;
        var username = e.ChatMessage.Username;
        if (message == null) return;
        if (username == null) return;
        // remove whitespace and ignore case
        message = UnicodeControlCharacters().Replace(message, string.Empty).ToLower().Trim();
        
        lock (LockObject)
        {
            // ignore duplicate messages
            if (UserMessages.TryGetValue(username, out var userMessages))
            {
                if (userMessages.Contains(message))
                {
                    WriteLine("Ignoring duplicate message from " + username);
                    return;
                }
            }
            else
            {
                UserMessages[username] = new List<string>();
            }
            UserMessages[username].Add(message);
            
            if (!MessageCounts.ContainsKey(message))
            {
                WriteLine("Adding answer from " + username + ": " + message);
                MessageCounts[message] = 1;
            }
            else
            {
                MessageCounts[message]++;
            }
        }
    }

    private static void UpdateOutputMessages()
    {
        lock (LockObject)
        {
            var mostFrequentMessages = MessageCounts.OrderByDescending(x => x.Value).Take(3);
            var keyValuePairs = mostFrequentMessages.ToList();
            if (!keyValuePairs.Any()) return;

            var output = keyValuePairs.Aggregate(
                "Answers:\r\n", 
                (current, mostFrequentMessage) => 
                    current + $"{mostFrequentMessage.Key}({mostFrequentMessage.Value})\r\n");
            WriteLine("Updating output file");
            File.WriteAllText("output.txt", output);
        }
    }

    private static void ResetMessageCounts()
    {
        lock (LockObject)
        {
            MessageCounts.Clear();
            UserMessages.Clear();
            File.WriteAllText("output.txt", "Answers:");
            Console.WriteLine("Cleared Message Log");
        }
    }

    private static void WriteLine(string message)
    {
        // clear current line
        if (UserInputBuffer.Count == 0)
        {
            Console.WriteLine(message);
        }
        else
        {
            ClearCurrentConsoleLine();
            Console.WriteLine(message);
            WriteInputBuffer();
        }
    }

    private static void WriteInputBuffer()
    {
        foreach (var key in UserInputBuffer)
        {
            Console.Write(key);
        }
    }

    private static void ClearCurrentConsoleLine()
    {
        var currentLineCursor = Console.CursorTop;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(new string(' ', Console.WindowWidth)); 
        Console.SetCursorPosition(0, currentLineCursor);
    }

    private static void ProcessInput(object? state)
    {
        while (_running)
        {
            // Check if a key is available to read without blocking
            if (Console.KeyAvailable)
            {
                // Read the key
                var key = Console.ReadKey(intercept: true);

                switch (key.Key)
                {
                    // Check if Enter key is pressed
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        // Process the entire user input
                        ProcessUserInput();
                        break;
                    case ConsoleKey.Backspace:
                    {
                        // remove key from buffer
                        lock (InputLockObject)
                        {
                            if (UserInputBuffer.Count > 0)
                            {
                                UserInputBuffer.RemoveAt(UserInputBuffer.Count - 1);
                                ClearCurrentConsoleLine();
                                WriteInputBuffer();
                            }
                        }
                        break;
                    }
                    default:
                    {
                        // Add the typed character to the buffer and display it
                        lock (InputLockObject)
                        {
                            UserInputBuffer.Add(key.KeyChar);
                            Console.Write(key.KeyChar);
                        }
                        break;
                    }
                }
            }
        }
    }

    private static void ProcessUserInput()
    {
        lock (InputLockObject)
        {
            // Convert the buffer to a string and display it
            var userInput = new string(UserInputBuffer.ToArray());
            switch (userInput)
            {
                case "clear":
                    ResetMessageCounts();
                    break;
                case "exit":
                    _running = false;
                    break;
                default:
                    Console.WriteLine("Unknown command: " + userInput);
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("clear - clear message logs");
                    Console.WriteLine("exit - exit the program");
                    break;
            }

            // Clear the buffer
            UserInputBuffer.Clear();
        }
    }

    [GeneratedRegex("\\p{C}+")]
    private static partial Regex UnicodeControlCharacters();
}
