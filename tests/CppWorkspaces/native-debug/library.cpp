#if defined(_WIN32)
#define FIXTURE_EXPORT __declspec(dllexport)
#else
#define FIXTURE_EXPORT
#endif

extern "C" FIXTURE_EXPORT int shared_answer(int value)
{
    return value + 1; // library-stop
}
