import std;

int main() {
    std::vector<int> numbers{40, 2};
    return numbers[0] + numbers[1] == 42 ? 0 : 1;
}
