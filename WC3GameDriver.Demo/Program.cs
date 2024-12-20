namespace WC3GameDriver.Demo
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            var bridge = new WC3LuaBridge("blankMap_lua.w3x");
            await bridge.RestartGameAsync();
            bridge.LuaResponseReceived += Bridge_LuaResponseReceived;
            bridge.ExecuteMain();

            while (true)
            {
                Console.Write("Enter Lua code to execute: ");
                string luaCode = Console.ReadLine();
                bridge.InjectAndExecuteLuaCode(luaCode);
                await Task.Delay(1000);
            }
        }

        private static void Bridge_LuaResponseReceived(int fileCounter, string response)
        {
            Console.WriteLine($"Lua response for request #{fileCounter} received: {response}");
        }
    }
}
