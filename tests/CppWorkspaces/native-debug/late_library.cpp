extern "C" __declspec(dllexport) int late_answer(int seed)
{
    return seed + 5; // late-library-stop
}
