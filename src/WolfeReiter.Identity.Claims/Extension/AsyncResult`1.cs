namespace WolfeReiter.Threading.Tasks
{
    [System.Obsolete("Role claims come from the token groups claim and are no longer cached per user.", error: true)]
    public class AsyncResult<T> where T : class
    {
        public AsyncResult()
        {
            Success = false;
        }
        public bool Success { get; set; }
        public T? Value { get; set; }
    }
}