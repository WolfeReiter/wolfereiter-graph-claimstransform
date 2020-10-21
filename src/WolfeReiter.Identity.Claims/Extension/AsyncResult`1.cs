namespace WolfeReiter.Threading.Tasks
{
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