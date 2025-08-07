using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace MyDiscordBot.Commands
{
    public class FancyTextCommand : ILegacyCommand
    {
        public string Name => "fancy";
        public string Description => "Transforms text into fancy styles: fullwidth, gothic, cursive, or custom emoji letters";

        public async Task ExecuteAsync(SocketMessage message, string[] args)
        {
            if (args.Length == 0)
            {
                await message.Channel.SendMessageAsync("❗ Usage: `!fancy [style] your text here`");
                return;
            }

            string style = args[0].ToLower();
            string input = string.Join(" ", args[1..]);

            string transformed = style switch
            {
                "fullwidth" => ToFullWidth(input),
                "gothic" => MapCharacters(input, GothicMap),
                "cursive" => MapCharacters(input, CursiveMap),
                "channel" => MapCharacters(input, ChannelMap),
                "custom" => ToCustomStyle(input),
                _ => ToFullWidth(string.Join(" ", args)) // fallback
            };

            await message.Channel.SendMessageAsync(transformed);
        }

        private string ToFullWidth(string input)
        {
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                if (c == ' ')
                    sb.Append('　'); // Fullwidth space
                else if (c >= 33 && c <= 126)
                    sb.Append((char)(c - 33 + 65281));
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private string ToCustomStyle(string input)
        {
            var sb = new StringBuilder();
            foreach (char c in input.ToUpper())
            {
                if (CustomMap.TryGetValue(c, out var fancy))
                    sb.Append(fancy);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private string MapCharacters(string input, Dictionary<char, string> map)
        {
            var sb = new StringBuilder();
            foreach (char c in input)
            {
                if (map.TryGetValue(c, out var fancy))
                    sb.Append(fancy);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        private static readonly Dictionary<char, string> GothicMap = new()
        {
            // Lowercase
            ['a'] = "𝔞",
            ['b'] = "𝔟",
            ['c'] = "𝔠",
            ['d'] = "𝔡",
            ['e'] = "𝔢",
            ['f'] = "𝔣",
            ['g'] = "𝔤",
            ['h'] = "𝔥",
            ['i'] = "𝔦",
            ['j'] = "𝔧",
            ['k'] = "𝔨",
            ['l'] = "𝔩",
            ['m'] = "𝔪",
            ['n'] = "𝔫",
            ['o'] = "𝔬",
            ['p'] = "𝔭",
            ['q'] = "𝔮",
            ['r'] = "𝔯",
            ['s'] = "𝔰",
            ['t'] = "𝔱",
            ['u'] = "𝔲",
            ['v'] = "𝔳",
            ['w'] = "𝔴",
            ['x'] = "𝔵",
            ['y'] = "𝔶",
            ['z'] = "𝔷",

            // Uppercase
            ['A'] = "𝔄",
            ['B'] = "𝔅",
            ['C'] = "ℭ",
            ['D'] = "𝔇",
            ['E'] = "𝔈",
            ['F'] = "𝔉",
            ['G'] = "𝔊",
            ['H'] = "ℌ",
            ['I'] = "ℑ",
            ['J'] = "𝔍",
            ['K'] = "𝔎",
            ['L'] = "𝔏",
            ['M'] = "𝔐",
            ['N'] = "𝔑",
            ['O'] = "𝔒",
            ['P'] = "𝔓",
            ['Q'] = "𝔔",
            ['R'] = "ℜ",
            ['S'] = "𝔖",
            ['T'] = "𝔗",
            ['U'] = "𝔘",
            ['V'] = "𝔙",
            ['W'] = "𝔚",
            ['X'] = "𝔛",
            ['Y'] = "𝔜",
            ['Z'] = "ℨ"
        };

        private static readonly Dictionary<char, string> CursiveMap = new()
        {
            // Lowercase
            ['a'] = "𝓪",
            ['b'] = "𝓫",
            ['c'] = "𝓬",
            ['d'] = "𝓭",
            ['e'] = "𝓮",
            ['f'] = "𝓯",
            ['g'] = "𝓰",
            ['h'] = "𝓱",
            ['i'] = "𝓲",
            ['j'] = "𝓳",
            ['k'] = "𝓴",
            ['l'] = "𝓵",
            ['m'] = "𝓶",
            ['n'] = "𝓷",
            ['o'] = "𝓸",
            ['p'] = "𝓹",
            ['q'] = "𝓺",
            ['r'] = "𝓻",
            ['s'] = "𝓼",
            ['t'] = "𝓽",
            ['u'] = "𝓾",
            ['v'] = "𝓿",
            ['w'] = "𝔀",
            ['x'] = "𝔁",
            ['y'] = "𝔂",
            ['z'] = "𝔃",

            // Uppercase
            ['A'] = "𝓐",
            ['B'] = "𝓑",
            ['C'] = "𝓒",
            ['D'] = "𝓓",
            ['E'] = "𝓔",
            ['F'] = "𝓕",
            ['G'] = "𝓖",
            ['H'] = "𝓗",
            ['I'] = "𝓘",
            ['J'] = "𝓙",
            ['K'] = "𝓚",
            ['L'] = "𝓛",
            ['M'] = "𝓜",
            ['N'] = "𝓝",
            ['O'] = "𝓞",
            ['P'] = "𝓟",
            ['Q'] = "𝓠",
            ['R'] = "𝓡",
            ['S'] = "𝓢",
            ['T'] = "𝓣",
            ['U'] = "𝓤",
            ['V'] = "𝓥",
            ['W'] = "𝓦",
            ['X'] = "𝓧",
            ['Y'] = "𝓨",
            ['Z'] = "𝓩"
        };

        private static readonly Dictionary<char, string> ChannelMap = new()
        {
            // Lowercase
            ['a'] = "𝓪",
            ['b'] = "𝓫",
            ['c'] = "𝓬",
            ['d'] = "𝓭",
            ['e'] = "𝓮",
            ['f'] = "𝓯",
            ['g'] = "𝓰",
            ['h'] = "𝓱",
            ['i'] = "𝓲",
            ['j'] = "𝓳",
            ['k'] = "𝓴",
            ['l'] = "𝓵",
            ['m'] = "𝓶",
            ['n'] = "𝓷",
            ['o'] = "𝓸",
            ['p'] = "𝓹",
            ['q'] = "𝓺",
            ['r'] = "𝓻",
            ['s'] = "𝓼",
            ['t'] = "𝓽",
            ['u'] = "𝓾",
            ['v'] = "𝓿",
            ['w'] = "𝔀",
            ['x'] = "𝔁",
            ['y'] = "𝔂",
            ['z'] = "𝔃",

            // Uppercase
            ['A'] = "𝓐",
            ['B'] = "𝓑",
            ['C'] = "𝓒",
            ['D'] = "𝓓",
            ['E'] = "𝓔",
            ['F'] = "𝓕",
            ['G'] = "𝓖",
            ['H'] = "𝓗",
            ['I'] = "𝓘",
            ['J'] = "𝓙",
            ['K'] = "𝓚",
            ['L'] = "𝓛",
            ['M'] = "𝓜",
            ['N'] = "𝓝",
            ['O'] = "𝓞",
            ['P'] = "𝓟",
            ['Q'] = "𝓠",
            ['R'] = "𝓡",
            ['S'] = "𝓢",
            ['T'] = "𝓣",
            ['U'] = "𝓤",
            ['V'] = "𝓥",
            ['W'] = "𝓦",
            ['X'] = "𝓧",
            ['Y'] = "𝓨",
            ['Z'] = "𝓩"
        };

        private static readonly Dictionary<char, string> CustomMap = new()
        {
            ['A'] = "🄰",
            ['B'] = "🄱",
            ['C'] = "🄲",
            ['D'] = "🄳",
            ['E'] = "🄴",
            ['F'] = "🄵",
            ['G'] = "🄶",
            ['H'] = "🄷",
            ['I'] = "🄸",
            ['J'] = "🄹",
            ['K'] = "🄺",
            ['L'] = "🄻",
            ['M'] = "🄼",
            ['N'] = "🄽",
            ['O'] = "🄾",
            ['P'] = "🄿",
            ['Q'] = "🅀",
            ['R'] = "🅁",
            ['S'] = "🅂",
            ['T'] = "🅃",
            ['U'] = "🅄",
            ['V'] = "🅅",
            ['W'] = "🅆",
            ['X'] = "🅇",
            ['Y'] = "🅈",
            ['Z'] = "🅉"
        };
    }
}