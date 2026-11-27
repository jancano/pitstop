#include "answer.hpp"
#include <cstdio>

int main() {
    std::printf("Pitstop template %d\n", answer());
    return answer() == 42 ? 0 : 1;
}
