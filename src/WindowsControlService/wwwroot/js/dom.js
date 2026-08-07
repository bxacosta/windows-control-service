/**
 * Building elements instead of assembling HTML strings. Everything shown here comes from the
 * machine -- executable paths, product names, user names -- and textContent cannot be talked
 * into being markup.
 */

/**
 * @param {string} tag
 * @param {Record<string, string | boolean>} attributes `text` sets textContent.
 * @param {Array<Node | string>} children
 */
export function el(tag, attributes = {}, children = []) {
  const node = document.createElement(tag);

  for (const [name, value] of Object.entries(attributes)) {
    if (name === 'text') {
      node.textContent = value;
    } else if (name === 'class') {
      node.className = value;
    } else if (value === true) {
      node.setAttribute(name, '');
    } else if (value !== false && value !== null && value !== undefined) {
      node.setAttribute(name, value);
    }
  }

  node.append(...children);

  return node;
}

/** Replaces the children of a container in one go, so the page never shows a half-built list. */
export function replace(container, children) {
  container.replaceChildren(...children);
}
