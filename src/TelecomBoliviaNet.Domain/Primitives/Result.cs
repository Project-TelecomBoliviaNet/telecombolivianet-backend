namespace TelecomBoliviaNet.Domain.Primitives;

public class Result
{
    public bool IsSuccess { get; protected set; }
    public string ErrorMessage { get; protected set; } = string.Empty;

    protected Result() { }

    private Result(bool isSuccess, string errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static Result Success() => new(true, string.Empty);
    public static Result Failure(string message) => new(false, message);
}

// BUG FIX: Result<T> hereda de Result para eliminar duplicación de IsSuccess/ErrorMessage
// y permitir polimorfismo: Result baseResult = someTypedResult
public class Result<T> : Result
{
    public T? Value { get; private set; }

    private Result(bool isSuccess, T? value, string errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
    }

    public static Result<T> Success(T value) => new(true, value, string.Empty);
    public new static Result<T> Failure(string message) => new(false, default, message);
}
