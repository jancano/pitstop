module debug_values;
import :detail;

Answer make_answer(int input)
{
    int scaled = public_scale(input); // implementation-stop
    int incremented = increment(scaled); // partition-call
    return {finish(incremented)}; // internal-call
}
