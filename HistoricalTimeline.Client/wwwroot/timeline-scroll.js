const bindings = new WeakMap();

export function synchronize(topScroll, contentScroll) {
    dispose(topScroll, contentScroll);

    let synchronizing = false;
    const syncTop = () => {
        if (!synchronizing) {
            synchronizing = true;
            topScroll.scrollLeft = contentScroll.scrollLeft;
            synchronizing = false;
        }
    };
    const syncContent = () => {
        if (!synchronizing) {
            synchronizing = true;
            contentScroll.scrollLeft = topScroll.scrollLeft;
            synchronizing = false;
        }
    };

    topScroll.addEventListener("scroll", syncContent);
    contentScroll.addEventListener("scroll", syncTop);
    bindings.set(contentScroll, { topScroll, syncTop, syncContent });
}

export function dispose(topScroll, contentScroll) {
    const binding = bindings.get(contentScroll);
    if (binding) {
        binding.topScroll.removeEventListener("scroll", binding.syncContent);
        contentScroll.removeEventListener("scroll", binding.syncTop);
        bindings.delete(contentScroll);
    }
}
