import debug_facade;
import std;

int main()
{
    Answer answer = calculate(9); // consumer-stop
    std::vector<int> values{answer.number};
    std::cout << values.front() << '\n'; // std-stop
    return values.front() == 21 ? 0 : 1;
}
