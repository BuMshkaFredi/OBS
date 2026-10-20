using System.Text;
using CustomerMail;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // Перенаправленный вывод может не поддерживать смену кодировки; это не критично.
}

return await Cli.RunAsync(args).ConfigureAwait(false);
