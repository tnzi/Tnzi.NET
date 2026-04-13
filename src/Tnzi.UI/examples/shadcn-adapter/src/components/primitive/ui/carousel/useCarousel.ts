import type { UnwrapRefCarouselApi as CarouselApi, CarouselEmits, CarouselProps } from "./interface"
import type { EmblaCarouselType } from "embla-carousel"
import type { EmblaCarouselVueType } from "embla-carousel-vue"
import type { Ref } from "vue"
import { createInjectionState } from "@vueuse/core"
import emblaCarouselVue from "embla-carousel-vue"
import { onMounted, ref } from "vue"

interface CarouselContext {
  carouselRef: EmblaCarouselVueType[0]
  carouselApi: Ref<EmblaCarouselType | undefined>
  canScrollPrev: Ref<boolean>
  canScrollNext: Ref<boolean>
  scrollPrev: () => void
  scrollNext: () => void
  orientation: NonNullable<CarouselProps["orientation"]>
}

const [useProvideCarousel, useInjectCarousel] = createInjectionState(
  ({
    opts,
    orientation,
    plugins,
  }: CarouselProps, emits: CarouselEmits): CarouselContext => {
    const [emblaNode, emblaApi] = emblaCarouselVue({
      ...opts,
      axis: orientation === "horizontal" ? "x" : "y",
    }, plugins)

    function scrollPrev() {
      emblaApi.value?.scrollPrev()
    }
    function scrollNext() {
      emblaApi.value?.scrollNext()
    }

    const canScrollNext = ref(false)
    const canScrollPrev = ref(false)

    function onSelect(api: CarouselApi) {
      canScrollNext.value = api?.canScrollNext() || false
      canScrollPrev.value = api?.canScrollPrev() || false
    }

    onMounted(() => {
      if (!emblaApi.value)
        return

      emblaApi.value?.on("init", onSelect)
      emblaApi.value?.on("reInit", onSelect)
      emblaApi.value?.on("select", onSelect)

      emits("init-api", emblaApi.value)
    })

    return {
      carouselRef: emblaNode,
      carouselApi: emblaApi,
      canScrollPrev,
      canScrollNext,
      scrollPrev,
      scrollNext,
      orientation: orientation ?? "horizontal",
    }
  },
)

function useCarousel(): CarouselContext {
  const carouselState = useInjectCarousel()

  if (!carouselState)
    throw new Error("useCarousel must be used within a <Carousel />")

  return carouselState
}

export { useCarousel, useProvideCarousel }
