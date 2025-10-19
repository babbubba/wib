using System.Diagnostics.CodeAnalysis;

namespace WIB.Application.Common;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// This pattern allows for explicit error handling without exceptions.
/// </summary>
/// <typeparam name="T">The type of the success value</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;
    private readonly string? _error;
    private readonly bool _isSuccess;

    private Result(T value)
    {
        _value = value;
        _error = null;
        _isSuccess = true;
    }

    private Result(string error)
    {
        _value = default;
        _error = error ?? "Unknown error";
        _isSuccess = false;
    }

    /// <summary>
    /// True if the operation was successful
    /// </summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// True if the operation failed
    /// </summary>
    [MemberNotNullWhen(false, nameof(Value))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isSuccess;

    /// <summary>
    /// The success value (only available when IsSuccess is true)
    /// </summary>
    public T? Value => _value;

    /// <summary>
    /// The error message (only available when IsFailure is true)
    /// </summary>
    public string? Error => _error;

    /// <summary>
    /// Creates a successful result with the given value
    /// </summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with the given error message
    /// </summary>
    public static Result<T> Failure(string error) => new(error);

    /// <summary>
    /// Creates a failed result from an exception
    /// </summary>
    public static Result<T> Failure(Exception exception) => new(exception.Message);

    /// <summary>
    /// Transforms the success value using the provided function
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (IsFailure)
            return Result<TNew>.Failure(_error);

        try
        {
            return Result<TNew>.Success(mapper(_value));
        }
        catch (Exception ex)
        {
            return Result<TNew>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Chains operations that return Results
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        if (IsFailure)
            return Result<TNew>.Failure(_error);

        try
        {
            return binder(_value);
        }
        catch (Exception ex)
        {
            return Result<TNew>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
        {
            try
            {
                action(_value);
            }
            catch (Exception ex)
            {
                return Failure(ex.Message);
            }
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure
    /// </summary>
    public Result<T> OnFailure(Action<string> action)
    {
        if (IsFailure)
        {
            action(_error);
        }
        return this;
    }

    /// <summary>
    /// Returns the value if successful, otherwise returns the default value
    /// </summary>
    public T GetValueOrDefault(T defaultValue = default!) 
        => IsSuccess ? _value : defaultValue;

    /// <summary>
    /// Returns the value if successful, otherwise throws an exception
    /// </summary>
    public T GetValueOrThrow()
    {
        if (IsFailure)
            throw new InvalidOperationException(_error);
        return _value;
    }

    /// <summary>
    /// Implicit conversion from T to Result<T>
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>
    /// Explicit conversion from Result<T> to T (throws on failure)
    /// </summary>
    public static explicit operator T(Result<T> result) => result.GetValueOrThrow();

    public override string ToString()
        => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
}

/// <summary>
/// Result without value - represents success or failure of an operation
/// </summary>
public readonly struct Result
{
    private readonly string? _error;
    private readonly bool _isSuccess;

    private Result(bool isSuccess, string? error = null)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    /// <summary>
    /// True if the operation was successful
    /// </summary>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => _isSuccess;

    /// <summary>
    /// True if the operation failed
    /// </summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !_isSuccess;

    /// <summary>
    /// The error message (only available when IsFailure is true)
    /// </summary>
    public string? Error => _error;

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static Result Success() => new(true);

    /// <summary>
    /// Creates a failed result with the given error message
    /// </summary>
    public static Result Failure(string error) => new(false, error ?? "Unknown error");

    /// <summary>
    /// Creates a failed result from an exception
    /// </summary>
    public static Result Failure(Exception exception) => new(false, exception.Message);

    /// <summary>
    /// Chains operations that return Results
    /// </summary>
    public Result Bind(Func<Result> binder)
    {
        if (IsFailure)
            return this;

        try
        {
            return binder();
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    /// <summary>
    /// Transforms to Result<T>
    /// </summary>
    public Result<T> Map<T>(Func<T> mapper)
    {
        if (IsFailure)
            return Result<T>.Failure(_error);

        try
        {
            return Result<T>.Success(mapper());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                return Failure(ex.Message);
            }
        }
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure
    /// </summary>
    public Result OnFailure(Action<string> action)
    {
        if (IsFailure)
        {
            action(_error);
        }
        return this;
    }

    public override string ToString()
        => IsSuccess ? "Success" : $"Failure({_error})";
}

/// <summary>
/// Static helper methods for creating Results
/// </summary>
public static class ResultHelpers
{
    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static Result Ok() => Result.Success();

    /// <summary>
    /// Creates a successful result with value
    /// </summary>
    public static Result<T> Ok<T>(T value) => Result<T>.Success(value);

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static Result Fail(string error) => Result.Failure(error);

    /// <summary>
    /// Creates a failed result with value type
    /// </summary>
    public static Result<T> Fail<T>(string error) => Result<T>.Failure(error);

    /// <summary>
    /// Creates a result from an operation that might throw
    /// </summary>
    public static Result Try(Action action)
    {
        try
        {
            action();
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Creates a result from a function that might throw
    /// </summary>
    public static Result<T> Try<T>(Func<T> func)
    {
        try
        {
            return Ok(func());
        }
        catch (Exception ex)
        {
            return Fail<T>(ex.Message);
        }
    }

    /// <summary>
    /// Creates a result from an async operation that might throw
    /// </summary>
    public static async Task<Result> TryAsync(Func<Task> asyncAction)
    {
        try
        {
            await asyncAction();
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Creates a result from an async function that might throw
    /// </summary>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> asyncFunc)
    {
        try
        {
            var result = await asyncFunc();
            return Ok(result);
        }
        catch (Exception ex)
        {
            return Fail<T>(ex.Message);
        }
    }
}
