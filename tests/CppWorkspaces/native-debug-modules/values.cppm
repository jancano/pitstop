export module debug_values;
export import :partition;

export struct Answer { int number; };

export Answer make_answer(int input);

export int public_scale(int input)
{
    int doubled = input * 2;
    return doubled; // interface-stop
}
