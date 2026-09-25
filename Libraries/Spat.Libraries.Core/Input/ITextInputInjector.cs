namespace Spat.Libraries.Core.Input;

public interface ITextInputInjector
{
    Task TypeAsync(string text, CancellationToken cancellationToken = default);
}
